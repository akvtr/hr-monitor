using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestPresentationAndOsc(string folder)
    {
        Check(ConnectionUpdate.Classify(new UnauthorizedAccessException("AccessDenied")) == ConnectionProblem.Permission, "permission failure has actionable category");
        Check(ConnectionUpdate.Classify(new UnauthorizedAccessException("SharingViolation")) == ConnectionProblem.Busy, "sharing violation differs from permission denial");
        Check(ConnectionUpdate.Classify(new Exception("Unreachable")) == ConnectionProblem.Unreachable, "unreachable category");
        Check(ConnectionUpdate.Classify(new OperationCanceledException()) == ConnectionProblem.Timeout, "timeout category");
        Check(ConnectionUpdate.Classify(new TimeoutException("ISOLATED TIMEOUT")) == ConnectionProblem.Timeout, "timeout exceptions are classified by type independently of localized message text");
        Words.Language = "en";
        Check(ConnectionUpdate.Name(ConnectionPhase.AwaitingData).Contains("awaiting") && ConnectionUpdate.Name(ConnectionPhase.Receiving).Contains("Receiving"), "service connection differs from actual samples in English");
        Check(ConnectionUpdate.Advice(ConnectionProblem.Permission).Contains("permissions"), "English recovery advice");
        Words.Language = "ja";
        var config = new Settings { Language = "en", CloseToTray = false };
        var path = Path.Combine(folder, "language.json"); config.Save(path);
        Check(Settings.Load(path).Language == "en" && !Settings.Load(path).CloseToTray, "language and tray preference survive restart");
        config.Language = "xx"; Throws(config.Validate, "unsupported language rejected"); config.Language = "ja";
        var database = Path.Combine(folder, "osc-test-isolation.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            receiver.Client.ReceiveTimeout = 2000;
            config.Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;
            var engine = new RecordingEngine(database, config);
            OscTransmission sent = null; engine.Transmitted += value => sent = value;
            engine.TestAsync(80).GetAwaiter().GetResult();
            IPEndPoint remote = null;
            var integer = receiver.Receive(ref remote); var percent = receiver.Receive(ref remote);
            Check(Type(integer) == 'i' && Bits(integer) == 80 && Type(percent) == 'f' && Bits(percent) == BitConverter.ToInt32(BitConverter.GetBytes(0.4f), 0), "one-shot test sends configured Int and Float datagrams");
            Check(sent != null && sent.Test && sent.Integer == 80 && sent.Percent == 0.4f && sent.Endpoint.EndsWith(":" + config.Port), "OSC status reports actual test payload and endpoint");
            using (var store = new Store(database)) Check(store.Bounds() == null && store.Query(0, long.MaxValue).Count == 0, "OSC test never creates a stored measurement");
            config.OscEnabled = false; engine.Configure(config);
            Throws(() => engine.TestAsync(90).GetAwaiter().GetResult(), "OSC test respects disabled output");
            Check(receiver.Available == 0, "disabled OSC test sends no datagram");
            engine.Accept(new Reading { UnixMs = 1000, Bpm = 75, Device = "isolated test", Session = "test" });
            engine.Accept(new Reading { UnixMs = 2000, Bpm = 76, Device = "isolated test", Session = "test" });
            engine.StopAsync().GetAwaiter().GetResult();
            using (var store = new Store(database)) { var bounds = store.Bounds(); Check(bounds.From == 1000 && bounds.To == 2001, "history bounds include final sample in half-open window"); }
        }
    }

    private static void TestCustomOscTransmission(string folder)
    {
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            receiver.Client.ReceiveTimeout = 2000;
            var config = new Settings {
                Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port,
                IntParameter = "Custom/Bpm", FloatParameter = "/avatar/parameters/Custom/Percent",
                PercentMin = 100, PercentMax = 300
            };
            var engine = new RecordingEngine(Path.Combine(folder, "custom-osc.db"), config);
            OscTransmission sent = null;
            engine.Transmitted += value => sent = value;
            try {
                engine.TestAsync(175).GetAwaiter().GetResult();
                IPEndPoint remote = null;
                var integer = receiver.Receive(ref remote);
                var floating = receiver.Receive(ref remote);
                Check(sent != null && sent.Test && sent.IntAddress == Address(integer) && sent.FloatAddress == Address(floating) &&
                    sent.Integer == Bits(integer) && sent.Percent == BitConverter.ToSingle(BitConverter.GetBytes(Bits(floating)), 0),
                    "custom OSC status matches the actual transmitted addresses and payloads");
                Check(Address(integer) == "/avatar/parameters/Custom/Bpm" && Type(integer) == 'i' && Bits(integer) == 175 &&
                    Address(floating) == "/avatar/parameters/Custom/Percent" && Type(floating) == 'f' && sent.Percent == 0.375f,
                    "custom OSC transmission preserves configured names, types and conversion bounds");
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
        }
    }

    private static void TestOscReceiveTimeout(string folder)
    {
        string legacyPath = Path.Combine(folder, "osc-timeout-legacy.json");
        File.WriteAllText(legacyPath, "{\"OscEnabled\":true}");
        var legacy = Settings.Load(legacyPath);
        Check(new Settings().OscZeroOnTimeout, "new settings default to sending both OSC values as zero on timeout");
        Check(legacy.OscZeroOnTimeout && legacy.OscTimeoutSeconds == 5, "settings without an OSC timeout mode default to zeros after five seconds");
        Check(legacy.OscTimeoutInt == 0 && legacy.OscTimeoutFloat == 0, "legacy settings preserve zero fallback payloads");
        foreach (int integer in new[] { -1, 256 })
            Throws(() => new Settings { OscTimeoutInt = integer }.Validate(), "invalid timeout Int fallback: " + integer);
        foreach (float floating in new[] { -0.01f, 1.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Throws(() => new Settings { OscTimeoutFloat = floating }.Validate(), "invalid timeout Float fallback: " + floating);
        new Settings { OscTimeoutInt = 255, OscTimeoutFloat = 1 }.Validate();
        File.WriteAllText(legacyPath, "{\"OscZeroOnTimeout\":false,\"OscTimeoutSeconds\":12}");
        var savedKeepLast = Settings.Load(legacyPath);
        Check(!savedKeepLast.OscZeroOnTimeout && savedKeepLast.OscTimeoutSeconds == 12, "an explicitly saved keep-last mode and interval are preserved");
        foreach (int seconds in new[] { 0, -1, 3601 }) {
            var invalid = new Settings { OscTimeoutSeconds = seconds };
            Throws(invalid.Validate, "invalid OSC receive timeout: " + seconds);
        }
        new Settings { OscTimeoutSeconds = 1 }.Validate();
        new Settings { OscTimeoutSeconds = 3600 }.Validate();
        string configPath = Path.Combine(folder, "osc-timeout-settings.json");
        new Settings { OscZeroOnTimeout = true, OscTimeoutSeconds = 12, OscTimeoutInt = 42, OscTimeoutFloat = 0.75f }.Save(configPath);
        var loaded = Settings.Load(configPath);
        Check(loaded.OscZeroOnTimeout && loaded.OscTimeoutSeconds == 12, "OSC timeout settings survive save and reload");
        Check(loaded.OscTimeoutInt == 42 && loaded.OscTimeoutFloat == 0.75f, "custom fallback values survive save and reload");

        string database = Path.Combine(folder, "osc-timeout.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            receiver.Client.ReceiveTimeout = 3000;
            var config = new Settings { Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port, OscZeroOnTimeout = false, OscTimeoutSeconds = 1, PercentMin = 40, PercentMax = 200,
                IntParameter = "Timeout/Int", FloatParameter = "Timeout/Float" };
            var transmissions = new System.Collections.Concurrent.ConcurrentQueue<OscTransmission>();
            var engine = new RecordingEngine(database, config);
            engine.Transmitted += transmissions.Enqueue;
            Action<int, float> receive = (integer, percent) => {
                IPEndPoint remote = null;
                byte[] a = receiver.Receive(ref remote), b = receiver.Receive(ref remote);
                Check(Address(a) == Settings.Address(config.IntParameter) && Type(a) == 'i' && Bits(a) == integer &&
                    Address(b) == Settings.Address(config.FloatParameter) && Type(b) == 'f' && Bits(b) == BitConverter.ToInt32(BitConverter.GetBytes(percent), 0),
                    "timeout pipeline emits both configured addresses, correct types and exact values");
            };
            try {
                engine.TestAsync(80).GetAwaiter().GetResult(); receive(80, 0.25f);
                Thread.Sleep(1200);
                Check(receiver.Available == 0, "keep-last mode leaves the receiver unchanged after the interval");
                config.OscZeroOnTimeout = true; engine.Configure(config);
                receive(0, 0);
                WaitUntil(() => transmissions.Any(t => t.Timeout));
                Check(transmissions.Last().Timeout && !transmissions.Last().Test, "startup silence produces an explicitly identified timeout transmission");
                using (var store = new Store(database)) Check(store.Bounds() == null, "timeout zeros do not fabricate a stored reading");
                Thread.Sleep(1200);
                Check(receiver.Available == 0, "a silent period sends zeros once without repeated packets");
                config.OscTimeoutInt = 42; config.OscTimeoutFloat = 0.75f; engine.Configure(config);
                receive(42, 0.75f);
                WaitUntil(() => transmissions.Last().Integer == 42);
                Check(transmissions.Last().Timeout && transmissions.Last().Percent == 0.75f, "changing fallback during silence sends raw configured values and matching timeout status");
                engine.Configure(config);
                Thread.Sleep(200);
                Check(receiver.Available == 0, "saving unchanged fallback values does not rearm timeout transmission");
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 80 }); receive(80, 0.25f);
                Thread.Sleep(600);
                engine.Accept(new Reading { UnixMs = 2000, Bpm = 0, Contact = false });
                Thread.Sleep(600);
                Check(receiver.Available == 0, "even a no-contact notification restarts the receive timer without sending a live value");
                receive(42, 0.75f);
                engine.Accept(new Reading { UnixMs = 3000, Bpm = 120 }); receive(120, 0.5f);
                WaitUntil(() => transmissions.Last().Integer == 120);
                Check(!transmissions.Last().Timeout, "valid reception resumes normal output after timeout zeros");
                config.OscZeroOnTimeout = false; engine.Configure(config);
                Thread.Sleep(1200);
                Check(receiver.Available == 0, "switching to keep-last cancels the pending timeout");
                config.OscEnabled = false; config.OscZeroOnTimeout = true; engine.Configure(config);
                Thread.Sleep(1200);
                Check(receiver.Available == 0, "disabled OSC suppresses timeout zeros");
                config.OscEnabled = true; engine.Configure(config); receive(42, 0.75f);
                engine.TestAsync(80).GetAwaiter().GetResult(); receive(80, 0.25f); receive(42, 0.75f);
                Check(transmissions.Count(t => t.Test) == 2, "one-shot tests stay separate and do not restart the receive timer");
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
            using (var store = new Store(database)) Check(store.Query(0, long.MaxValue).Count == 3, "only the three real notifications are stored across timeout and test transmissions");
        }
    }

    private static void TestOscSenderLifecycle()
    {
        using (var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var sender = new OscSender()) {
            first.Client.ReceiveTimeout = 2000; second.Client.ReceiveTimeout = 2000;
            var config = new Settings { Port = ((IPEndPoint)first.Client.LocalEndPoint).Port, PercentMin = 40, PercentMax = 200 };
            Check(config.Percent(int.MinValue) == 0 && config.Percent(int.MaxValue) == 1, "percent conversion clamps extreme integers without subtraction overflow");
            sender.Send(config, int.MinValue);
            IPEndPoint remote = null;
            Check(Bits(first.Receive(ref remote)) == 0 && Bits(first.Receive(ref remote)) == 0,
                "OSC negative extreme sends consistent zero integer and percent values");
            config.Port = ((IPEndPoint)second.Client.LocalEndPoint).Port;
            sender.Send(config, int.MaxValue);
            Check(Bits(second.Receive(ref remote)) == 255 && Bits(second.Receive(ref remote)) == BitConverter.ToInt32(BitConverter.GetBytes(1f), 0) && first.Available == 0,
                "OSC endpoint change routes both clamped values only to the new destination");
            config.Host = "invalid";
            Throws<FormatException>(() => sender.Send(config, 80), "OSC reports invalid endpoint configuration");
            config.Host = "127.0.0.1";
            sender.Send(config, 80);
            Check(Bits(second.Receive(ref remote)) == 80 && Bits(second.Receive(ref remote)) == BitConverter.ToInt32(BitConverter.GetBytes(0.25f), 0),
                "OSC sender recovers after an endpoint configuration failure");
            sender.Dispose(); sender.Dispose();
            config.Port = ((IPEndPoint)first.Client.LocalEndPoint).Port;
            Throws<ObjectDisposedException>(() => sender.Send(config, 80), "disposed OSC sender cannot reopen a socket for a changed endpoint");
            config.OscEnabled = false;
            Throws<ObjectDisposedException>(() => sender.Send(config, 80), "disabled output does not hide use of a disposed OSC sender");
            Throws<ObjectDisposedException>(() => sender.Send(config, new OscValues(config, 80)), "recording payloads also reject a disposed OSC sender");
            Check(first.Available == 0 && second.Available == 0, "disposed OSC sender emits no datagrams");
        }
    }

    private static void TestOscPacketBoundaries()
    {
        Check(OscSender.Packet("/", 'i', int.MaxValue).SequenceEqual(new byte[] { 47, 0, 0, 0, 44, 105, 0, 0, 127, 255, 255, 255 }),
            "OSC root address and maximum integer retain exact padding and network byte order");
        Check(OscSender.Packet("/xx", 'i', int.MinValue).SequenceEqual(new byte[] { 47, 120, 120, 0, 44, 105, 0, 0, 128, 0, 0, 0 }),
            "OSC address terminator aligns at the end of a block and preserves a negative integer");
        Check(OscSender.Packet("/xxx", 'f', 0x3F800000).SequenceEqual(new byte[] { 47, 120, 120, 120, 0, 0, 0, 0, 44, 102, 0, 0, 63, 128, 0, 0 }),
            "OSC address ending on a block boundary receives a complete terminator block");
        Check(OscSender.Packet("/xxxx", 'i', -1).SequenceEqual(new byte[] { 47, 120, 120, 120, 120, 0, 0, 0, 44, 105, 0, 0, 255, 255, 255, 255 }),
            "OSC address padding and all-one payload bytes remain exact");
        foreach (char type in new[] { 'c', 'r', 'm' }) {
            byte[] packet = OscSender.Packet("/x", type, 0x11223344);
            Check(Type(packet) == type && Bits(packet) == 0x11223344, "OSC retains existing fixed-width extension type " + type);
        }
        Throws<ArgumentNullException>(() => OscSender.Packet(null, 'i', 0), "OSC packet rejects a missing address explicitly");
        foreach (string address in new[] { "", "x", "/x\0tail", "/x y", "/心拍", "/x\n" })
            Throws<ArgumentException>(() => OscSender.Packet(address, 'i', 0), "OSC packet rejects malformed address " + address.Replace("\0", "\\0").Replace("\n", "\\n"));
        foreach (char type in new[] { 's', 'h', 'T', '\0', '\u0100' })
            Throws<ArgumentOutOfRangeException>(() => OscSender.Packet("/x", type, 0), "OSC packet rejects type tags that cannot encode one 32-bit argument: " + (int)type);
    }

    private static void TestOscPackets()
    {
        var settings = new Settings();
        var packet = OscSender.Packet("/x", 'i', 72);
        Check(packet.SequenceEqual(new byte[] { 47, 120, 0, 0, 44, 105, 0, 0, 0, 0, 0, 72 }), "OSC byte-for-byte encoding and alignment");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var sender = new OscSender()) {
            settings.Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port; receiver.Client.ReceiveTimeout = 2000;
            sender.Send(settings, 100); IPEndPoint remote = null;
            var a = receiver.Receive(ref remote); var b = receiver.Receive(ref remote);
            Check(Address(a) == "/avatar/parameters/VRCOSC/Heartrate/Value" && Type(a) == 'i' && Bits(a) == 100, "UDP Int address, type and value");
            Check(Address(b) == "/avatar/parameters/HRPercent" && Type(b) == 'f' && BitConverter.ToSingle(BitConverter.GetBytes(Bits(b)), 0) == 0.5f, "UDP Float address, type and value");
            sender.Send(settings, 300); a = receiver.Receive(ref remote); b = receiver.Receive(ref remote);
            Check(Bits(a) == 255 && BitConverter.ToSingle(BitConverter.GetBytes(Bits(b)), 0) == 1, "VRChat Int and Float range limits");
            settings.OscEnabled = false; sender.Send(settings, 80); Thread.Sleep(50); Check(receiver.Available == 0, "OSC disabled emits no datagrams");
        }
    }
}
