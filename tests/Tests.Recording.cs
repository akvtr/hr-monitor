using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestRecordingObserverFailures(string folder)
    {
        string database = Path.Combine(folder, "observer-failures.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            receiver.Client.ReceiveTimeout = 2000;
            var config = new Settings { Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port, OscZeroOnTimeout = false };
            var engine = new RecordingEngine(database, config);
            var errors = new List<string>();
            var processed = new List<Reading>();
            var transmissions = new List<OscTransmission>();
            var oscFailures = new List<string>();
            engine.Error += message => { throw new InvalidOperationException("error observer failure"); };
            engine.Error += errors.Add;
            engine.Processed += (row, storage, sending) => { throw new InvalidOperationException("processed observer failure"); };
            engine.Processed += (row, storage, sending) => processed.Add(row);
            engine.Transmitted += value => { throw new InvalidOperationException("transmitted observer failure"); };
            engine.Transmitted += transmissions.Add;
            engine.OscFailed += message => { throw new InvalidOperationException("OSC error observer failure"); };
            engine.OscFailed += oscFailures.Add;
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 70 });
                engine.Accept(new Reading { UnixMs = 2000, Bpm = 80 });
                Task successful = engine.TestAsync(90);
                Check(SpinWait.SpinUntil(() => successful.IsCompleted, 3000), "throwing observers never leave a queued OSC test pending");
                successful.GetAwaiter().GetResult();
                config.OscEnabled = false; engine.Configure(config);
                Task failed = engine.TestAsync(90);
                Check(SpinWait.SpinUntil(() => failed.IsCompleted, 3000), "throwing error observers never leave a failed OSC test pending");
                Throws<InvalidOperationException>(() => failed.GetAwaiter().GetResult(), "OSC test preserves its actual send failure despite observer failures");
                engine.Accept(new Reading { UnixMs = 3000, Bpm = 100 });
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
            Check(processed.Select(row => row.Bpm).SequenceEqual(new[] { 70, 80, 100 }), "later processed observers receive every reading despite earlier observer failures");
            Check(transmissions.Select(value => value.Integer).SequenceEqual(new[] { 70, 80, 90 }) && oscFailures.Count == 1,
                "successful transmissions stay successful and only the disabled test reports an OSC failure");
            Check(errors.Any(message => message.Contains("processed observer failure")) && errors.Any(message => message.Contains("transmitted observer failure")) && errors.Any(message => message.Contains("OSC error observer failure")),
                "observer failures reach later error observers without recursive reporting");
            using (var store = new Store(database))
                Check(store.Query(0, 4000).Count == 3, "observer failures do not interrupt saving subsequent readings");
            IPEndPoint remote = null;
            foreach (int expected in new[] { 70, 80, 90 }) {
                Check(Bits(receiver.Receive(ref remote)) == expected, "observer failure preserves actual UDP integer payload " + expected);
                receiver.Receive(ref remote);
            }
            Check(receiver.Available == 0, "observer failures do not cause duplicate UDP sends");
        }
    }

    private static void TestRecordingInputValidation(string folder)
    {
        string database = Path.Combine(folder, "invalid-recording-settings.db");
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using (var store = new Store(database))
            store.Add(new Reading { UnixMs = now, Bpm = 75, Device = "ISOLATED VALIDATION TEST" });
        Throws(() => {
            var invalid = new RecordingEngine(database, new Settings { OscEnabled = false, RetentionDays = -1 });
            invalid.StopAsync().GetAwaiter().GetResult();
        }, "recording startup rejects invalid retention before deleting data");
        using (var store = new Store(database))
            Check(store.Query(now, now + 1).Count == 1, "invalid startup retention preserves existing recordings");
        string missing = Path.Combine(folder, "invalid-engine", "new.db");
        Throws(() => {
            var invalid = new RecordingEngine(missing, new Settings { OscEnabled = false, Port = 0 });
            invalid.StopAsync().GetAwaiter().GetResult();
        }, "recording startup validates configuration even with OSC disabled");
        Check(!Directory.Exists(Path.GetDirectoryName(missing)), "invalid startup settings do not create database files or directories");
        var engine = new RecordingEngine(database, new Settings { OscEnabled = false });
        try {
            Throws(() => engine.Configure(new Settings { OscEnabled = false, RetentionDays = -1 }), "recording reconfiguration rejects invalid retention");
            bool rejectedNull = false;
            try { engine.Accept(null); }
            catch (ArgumentNullException) { rejectedNull = true; }
            Check(rejectedNull, "recording rejects null input synchronously before queuing it");
            engine.Accept(new Reading { UnixMs = now + 1, Bpm = 85 });
        } finally { engine.StopAsync().GetAwaiter().GetResult(); }
        using (var store = new Store(database))
            Check(store.Query(now, now + 2).Count == 2, "recording remains usable with unchanged retention after rejected inputs");
    }

    private static void TestRecordingReadingSnapshot(string folder)
    {
        string database = Path.Combine(folder, "reading-snapshot.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var processing = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            receiver.Client.ReceiveTimeout = 2000;
            var config = new Settings { Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port, OscZeroOnTimeout = false };
            var engine = new RecordingEngine(database, config);
            var processed = new List<Reading>();
            var transmissions = new List<OscTransmission>();
            engine.Transmitted += transmissions.Add;
            engine.Processed += (row, storage, sending) => {
                processed.Add(row);
                if (row.UnixMs == 1000) {
                    processing.Set();
                    if (!release.Wait(5000)) throw new TimeoutException("Snapshot test did not release the writer.");
                }
            };
            var input = new Reading { Id = 999, UnixMs = 2000, Bpm = 80, Contact = true, Session = "first", Device = "first sensor", Rr = "1000;500", Energy = 42 };
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 0 });
                Check(processing.Wait(2000), "reading snapshot test holds the writer before enqueueing mutable input");
                engine.Accept(input);
                input.UnixMs = 3000; input.Bpm = 100; input.Contact = null;
                input.Session = "second"; input.Device = "second sensor"; input.Rr = "750"; input.Energy = null;
                engine.Accept(input);
                input.UnixMs = 4000; input.Bpm = 0; input.Contact = false;
                input.Session = "changed"; input.Device = "changed"; input.Rr = ""; input.Energy = 0;
            } finally { release.Set(); engine.StopAsync().GetAwaiter().GetResult(); }

            using (var store = new Store(database)) {
                var rows = store.Query(0, 5000);
                Check(rows.Select(r => r.UnixMs).SequenceEqual(new long[] { 3000, 2000, 1000 }), "reused input retains the timestamp of each accepted notification");
                var first = rows.Single(r => r.UnixMs == 2000);
                Check(first.Bpm == 80 && first.Contact == true && first.Session == "first" && first.Device == "first sensor" && first.Rr == "1000;500" && first.Energy == 42,
                    "queued reading snapshot preserves every originally accepted measurement field");
                var second = rows.Single(r => r.UnixMs == 3000);
                Check(second.Bpm == 100 && second.Contact == null && second.Session == "second" && second.Device == "second sensor" && second.Rr == "750" && second.Energy == null,
                    "each acceptance owns a distinct snapshot including nullable fields");
            }
            Check(input.Id == 999 && processed.Count == 3 && processed.All(row => row.Id > 0 && !ReferenceEquals(row, input)) && processed[1].Id != processed[2].Id,
                "recording assigns IDs to owned readings without mutating caller input");
            Check(transmissions.Select(t => t.Integer).SequenceEqual(new[] { 80, 100 }), "OSC sends accepted values despite later input mutation");
            IPEndPoint remote = null;
            Check(Bits(receiver.Receive(ref remote)) == 80 && Bits(receiver.Receive(ref remote)) == BitConverter.ToInt32(BitConverter.GetBytes(0.4f), 0) &&
                Bits(receiver.Receive(ref remote)) == 100 && Bits(receiver.Receive(ref remote)) == BitConverter.ToInt32(BitConverter.GetBytes(0.5f), 0) && receiver.Available == 0,
                "actual UDP packets preserve both snapshots in queue order");
        }
    }

    private static void TestRecordingShutdown(string folder)
    {
        string database = Path.Combine(folder, "shutdown.db");
        using (var processing = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim()) {
            var engine = new RecordingEngine(database, new Settings { OscEnabled = false });
            engine.Processed += (row, storage, sending) => {
                if (row.UnixMs == 1000) {
                    processing.Set();
                    if (!release.Wait(5000)) throw new TimeoutException("Shutdown test did not release the writer.");
                }
            };
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 60 });
                Check(processing.Wait(2000), "shutdown test holds a live writer before requesting stop");
                engine.Accept(new Reading { UnixMs = 2000, Bpm = 70 });
                Task first = engine.StopAsync();
                Check(!first.IsCompleted && ReferenceEquals(first, engine.StopAsync()), "overlapping shutdown requests share the pending drain task");
                Task concurrent = Task.Run(() => engine.StopAsync());
                release.Set();
                Check(Task.WaitAll(new[] { first, concurrent }, 3000), "concurrent shutdown callers finish after the writer drains");
                Check(ReferenceEquals(first, engine.StopAsync()), "shutdown remains safe after queue disposal");
                using (var store = new Store(database))
                    Check(store.Query(0, 3000).Count == 2, "repeated shutdown preserves every accepted reading");
            } finally {
                release.Set();
                engine.StopAsync().GetAwaiter().GetResult();
            }
        }
    }

    private static void TestRecordingPipeline(string folder)
    {
        string database = Path.Combine(folder, "recording-pipeline.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            receiver.Client.ReceiveTimeout = 2000;
            var config = new Settings { Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port };
            var results = new List<string>();
            var transmissions = new List<OscTransmission>();
            var engine = new RecordingEngine(database, config);
            engine.Processed += (reading, storage, sending) => results.Add(storage + ":" + sending);
            engine.Transmitted += transmissions.Add;
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 80, Contact = null });
                engine.Accept(new Reading { UnixMs = 2000, Bpm = 90, Contact = false });
                engine.Accept(new Reading { UnixMs = 3000, Bpm = 0, Contact = true });
                engine.Accept(new Reading { UnixMs = 4000, Bpm = 300, Contact = true });
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
            Check(results.SequenceEqual(new[] { "saved:sent", "saved:waiting", "saved:waiting", "saved:sent" }), "recording pipeline saves every notification and suppresses invalid OSC samples in queue order");
            Check(transmissions.Count == 2 && transmissions.All(t => !t.Test) && transmissions[0].Integer == 80 && transmissions[1].Integer == 255 && transmissions[1].Percent == 1, "live OSC events identify real transmissions and report clamped payloads");
            IPEndPoint remote = null;
            Check(Bits(receiver.Receive(ref remote)) == 80, "live unknown-contact sample sends its actual integer value");
            receiver.Receive(ref remote);
            Check(Bits(receiver.Receive(ref remote)) == 255, "live high-rate sample clamps only its OSC integer");
            receiver.Receive(ref remote);
            Check(receiver.Available == 0, "zero and absent-contact samples emit no extra OSC datagrams");
            using (var store = new Store(database))
                Check(store.Query(0, long.MaxValue).Select(r => r.Bpm).SequenceEqual(new[] { 300, 0, 90, 80 }), "recording pipeline preserves raw readings even when OSC suppresses or clamps them");
        }

        string snapshotDatabase = Path.Combine(folder, "recording-config-snapshot.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var entered = new ManualResetEventSlim())
        using (var resume = new ManualResetEventSlim()) {
            receiver.Client.ReceiveTimeout = 2000;
            var config = new Settings { Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port };
            var engine = new RecordingEngine(snapshotDatabase, config);
            var results = new List<string>();
            int failures = 0;
            engine.OscFailed += error => failures++;
            engine.Processed += (reading, storage, sending) => {
                results.Add(storage + ":" + sending);
                if (reading.UnixMs == 1000) { entered.Set(); resume.Wait(); }
            };
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 0 });
                Check(entered.Wait(3000), "isolated worker barrier is reached before queuing a test");
                var pendingTest = engine.TestAsync(80);
                config.OscEnabled = false;
                engine.Configure(config);
                resume.Set();
                Check(pendingTest.Wait(3000), "queued OSC test retains the configuration captured at request time");
                IPEndPoint remote = null;
                Check(Bits(receiver.Receive(ref remote)) == 80, "queued OSC test sends using its enabled configuration snapshot");
                receiver.Receive(ref remote);
                Throws(() => engine.TestAsync(90).GetAwaiter().GetResult(), "later OSC test uses the newly disabled configuration");
                engine.Accept(new Reading { UnixMs = 2000, Bpm = 75 });
            } finally {
                resume.Set();
                engine.StopAsync().GetAwaiter().GetResult();
            }
            Check(failures == 1 && results.SequenceEqual(new[] { "saved:waiting", "saved:disabled" }), "OSC test failure reports once and the worker continues saving with current settings");
            Check(receiver.Available == 0, "disabled live output and failed test emit no datagrams");
            using (var store = new Store(snapshotDatabase))
                Check(store.Query(0, long.MaxValue).Select(r => r.Bpm).SequenceEqual(new[] { 75, 0 }), "queued tests never persist readings even across configuration changes");
        }
    }

    private static void TestQueueDrain(string folder)
    {
        string database = Path.Combine(folder, "recording-drain.db");
        var settings = new Settings { OscEnabled = false, RetentionDays = 1 };
        var engine = new RecordingEngine(database, settings);
        var errors = new List<string>(); engine.Error += errors.Add;
        long queuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 200; i++) engine.Accept(new Reading { UnixMs = queuedAt + i, Bpm = 60 + i % 20, Device = "test", Session = "queue", Rr = "" });
        engine.StopAsync().GetAwaiter().GetResult();
        using (var reopened = new Store(database)) Check(errors.Count == 0 && reopened.Query(queuedAt, queuedAt + 200).Count == 200, "shutdown drains every queued reading");
    }
}
