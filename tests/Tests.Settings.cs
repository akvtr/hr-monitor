using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestSettingsValidation(string folder)
    {
        var settings = new Settings(); settings.Validate();
        Check(settings.AutoConnect && settings.Theme == "System", "automatic connection and system theme defaults");
        string oldConfig = Path.Combine(folder, "old-settings.json"); File.WriteAllText(oldConfig, "{\"Port\":9000}");
        Check(Settings.Load(oldConfig).AutoConnect && Settings.Load(oldConfig).Theme == "System", "old settings migrate without disabling auto-connect");
        var badTheme = settings.Copy(); badTheme.Theme = "invalid"; Throws(badTheme.Validate, "invalid theme rejected");
        Check(Settings.Address(settings.IntParameter) == "/avatar/parameters/VRCOSC/Heartrate/Value", "default Int OSC address");
        Check(Settings.Address(settings.FloatParameter) == "/avatar/parameters/HRPercent", "default Float OSC address");
        Check(Settings.Address("/avatar/parameters/HRPercent") == "/avatar/parameters/HRPercent", "full address not duplicated");
        Check(settings.Percent(100) == 0.5f && settings.Percent(250) == 1f && settings.Percent(-1) == 0, "percent default and clamp");
        var custom = settings.Copy(); custom.PercentMin = 40; custom.PercentMax = 180; Check(custom.Percent(110) == 0.5f, "custom percent range");
        custom.PercentMax = 40; Throws(custom.Validate, "invalid conversion interval");
        foreach (var invalid in new[] { "", "/input/Jump", "HR Percent", "HR*", "HR//Value", "心拍", "HR\0" }) Throws(() => Settings.Address(invalid), "invalid parameter: " + invalid);
        custom = settings.Copy(); custom.FloatParameter = custom.IntParameter; Throws(custom.Validate, "conflicting OSC parameters");
        custom = settings.Copy(); custom.Port = 0; Throws(custom.Validate, "invalid UDP port");
        custom = settings.Copy(); custom.Host = "not-an-ip"; Throws(custom.Validate, "invalid destination");
        string config = Path.Combine(folder, "settings.json"); settings.Save(config); settings.Port = 9010; settings.Save(config);
        Check(Settings.Load(config).Port == 9010 && File.Exists(config + ".bak"), "atomic settings replacement and backup");
    }

    private static void TestSettingsDeviceMetadata(string folder)
    {
        string path = Path.Combine(folder, "device-metadata.json");
        foreach (ulong address in new[] { 0UL, 1UL, 0xFFFFFFFFFFFFUL }) {
            var settings = new Settings { LastAddress = address };
            settings.Validate(); settings.Save(path);
            Check(Settings.Load(path).LastAddress == address, "settings preserve valid 48-bit Bluetooth address: " + address);
        }
        foreach (ulong address in new[] { 0x1000000000000UL, ulong.MaxValue }) {
            var settings = new Settings { LastAddress = address };
            Throws<ArgumentException>(settings.Validate, "settings reject Bluetooth addresses wider than 48 bits: " + address);
        }
        foreach (int type in new[] { -1, 0, 1, 2 }) {
            var settings = new Settings { LastAddressType = type };
            settings.Validate(); settings.Save(path);
            Check(Settings.Load(path).LastAddressType == type, "settings preserve unknown or defined Bluetooth address type: " + type);
        }
        foreach (int type in new[] { int.MinValue, -2, 3, int.MaxValue }) {
            File.WriteAllText(path, "{\"LastAddressType\":" + type + "}");
            Throws<ArgumentException>(() => Settings.Load(path), "settings reject an undefined saved Bluetooth address type before connection: " + type);
        }
    }

    private static void TestSettingsUnicodeDecoding(string folder)
    {
        string path = Path.Combine(folder, "unicode-settings.json");
        byte[] prefix = Encoding.UTF8.GetBytes("{\"LastName\":\"");
        byte[] suffix = Encoding.UTF8.GetBytes("\",\"RetentionDays\":0}");
        byte[] malformed = prefix.Concat(new byte[] { 0xC3, 0x28 }).Concat(suffix).ToArray();
        foreach (bool bom in new[] { false, true }) {
            byte[] bytes = bom ? Encoding.UTF8.GetPreamble().Concat(malformed).ToArray() : malformed;
            File.WriteAllBytes(path, bytes);
            Throws<DecoderFallbackException>(() => Settings.Load(path), "settings reject malformed UTF-8 instead of replacing saved device metadata silently; BOM=" + bom);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "failed settings decoding leaves the original file unchanged; BOM=" + bom);
        }
        string valid = "{\"LastName\":\"日本語\"}";
        File.WriteAllText(path, valid, new UTF8Encoding(true));
        Check(Settings.Load(path).LastName == "日本語", "settings accept a valid UTF-8 preamble and Unicode device name");
        File.WriteAllText(path, valid, Encoding.Unicode);
        Check(Settings.Load(path).LastName == "日本語", "settings retain support for valid Unicode files with a UTF-16 preamble");
        Encoding[] encodings = { Encoding.Unicode, Encoding.BigEndianUnicode, new UTF32Encoding(false, true), new UTF32Encoding(true, true) };
        byte[][] invalidCharacters = { new byte[] { 0x00, 0xD8 }, new byte[] { 0xD8, 0x00 }, new byte[] { 0x00, 0x00, 0x11, 0x00 }, new byte[] { 0x00, 0x11, 0x00, 0x00 } };
        const string name = "日本語\uD83D\uDE00";
        foreach (var encoding in encodings) {
            File.WriteAllText(path, "{\"LastName\":\"" + name + "\"}", encoding);
            Check(Settings.Load(path).LastName == name, "settings preserve supplementary Unicode with a recognized preamble: " + encoding.WebName);
        }
        for (int i = 0; i < encodings.Length; i++) {
            var encoding = encodings[i];
            byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes("{\"LastName\":\"")).Concat(invalidCharacters[i]).Concat(encoding.GetBytes("\"}")).ToArray();
            File.WriteAllBytes(path, bytes);
            Throws<DecoderFallbackException>(() => Settings.Load(path), "settings reject invalid Unicode scalar data after detecting a preamble: " + encoding.WebName);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "invalid Unicode settings remain unchanged after decoding failure: " + encoding.WebName);
            bytes = encoding.GetPreamble().Concat(encoding.GetBytes(valid)).ToArray();
            bytes = bytes.Take(bytes.Length - 1).ToArray();
            File.WriteAllBytes(path, bytes);
            Throws<DecoderFallbackException>(() => Settings.Load(path), "settings reject an incomplete final Unicode code unit: " + encoding.WebName);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "truncated Unicode settings remain unchanged after decoding failure: " + encoding.WebName);
        }
    }

    private static void TestSettingsPersistence(string folder)
    {
        string directory = Path.Combine(folder, "settings-persistence");
        string path = Path.Combine(directory, "settings.json");
        Check(Settings.Load(path).Port == 9000, "missing settings directory uses defaults");
        var settings = new Settings { LastName = "保存テスト", Port = 9010 };
        settings.Save(path);
        byte[] original = File.ReadAllBytes(path);
        Check(Settings.Load(path).LastName == settings.LastName && !File.Exists(path + ".bak"), "first settings save preserves Unicode without inventing a backup");
        File.WriteAllText(path + ".tmp", "unrelated file");
        using (var legacyTemporary = new FileStream(path + ".tmp", FileMode.Open, FileAccess.Read, FileShare.None)) {
            settings.Port = 9020;
            settings.Save(path);
        }
        Check(Settings.Load(path).Port == 9020 && File.ReadAllText(path + ".tmp") == "unrelated file",
            "settings save owns a private temporary file and preserves an existing sibling");
        Check(File.ReadAllBytes(path + ".bak").SequenceEqual(original), "settings replacement backs up the previous complete file");
        byte[] current = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
            settings.Port = 9030;
            Throws(() => settings.Save(path), "settings save reports a locked destination");
            Throws(() => Settings.Load(path), "settings load reports read failures without returning defaults");
        }
        Check(File.ReadAllBytes(path).SequenceEqual(current) && File.ReadAllBytes(path + ".bak").SequenceEqual(original),
            "failed settings replacement preserves both current settings and backup");
        settings.Port = 0;
        Throws(() => settings.Save(path), "invalid settings are rejected before replacing persisted data");
        Check(File.ReadAllBytes(path).SequenceEqual(current) && Directory.GetFiles(directory).Length == 3,
            "failed settings writes leave no temporary files or changed settings");
        Throws(() => Settings.Load(directory), "a settings path pointing to a directory is an error");
        string invalid = Path.Combine(directory, "invalid.json");
        foreach (string json in new[] { "", "null", "{", "{\"Port\":0}" }) {
            File.WriteAllText(invalid, json);
            Throws(() => Settings.Load(invalid), "existing empty, malformed or invalid settings never fall back silently: " + json);
        }
    }

    private static void TestAtomicFileCleanupFailure(string folder)
    {
        string directory = Path.Combine(folder, "cleanup-failure");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.txt");
        File.WriteAllText(path, "current");
        File.WriteAllText(path + ".bak", "previous");
        var writeFailure = new IOException("Isolated write failure before cleanup");
        Exception failure = null;
        string temporary = null;
        try {
            try {
                AtomicFile.Write(path, stream => {
                    temporary = ((FileStream)stream).Name;
                    if (!string.Equals(Path.GetDirectoryName(temporary), directory, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Temporary file escaped the isolated test directory.");
                    stream.WriteByte(1);
                    File.SetAttributes(temporary, FileAttributes.ReadOnly);
                    throw writeFailure;
                }, keepBackup: true);
            } catch (Exception ex) { failure = ex; }
            var combined = failure as AggregateException;
            Check(combined != null && combined.InnerExceptions.Count == 2 && ReferenceEquals(combined.InnerExceptions[0], writeFailure) && combined.InnerExceptions[1] is UnauthorizedAccessException,
                "atomic write preserves both the original write error and a failed temporary-file cleanup");
            Check(File.ReadAllText(path) == "current" && File.ReadAllText(path + ".bak") == "previous",
                "failed write and cleanup preserve both the destination and its backup");
            Check(File.Exists(temporary), "atomic write reports a cleanup failure without pretending the incomplete file was removed");
        } finally {
            if (temporary != null && string.Equals(Path.GetDirectoryName(temporary), directory, StringComparison.OrdinalIgnoreCase) && File.Exists(temporary)) {
                File.SetAttributes(temporary, FileAttributes.Normal);
                File.Delete(temporary);
            }
        }
        failure = null;
        try { AtomicFile.Write(path, stream => { throw writeFailure; }); }
        catch (Exception ex) { failure = ex; }
        Check(ReferenceEquals(failure, writeFailure) && Directory.GetFiles(directory).Length == 2,
            "successful cleanup rethrows the original exception without replacing it");
        AtomicFile.Write(path, stream => { using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 1024, true)) writer.Write("recovered"); }, keepBackup: true);
        Check(File.ReadAllText(path) == "recovered" && File.ReadAllText(path + ".bak") == "current" && Directory.GetFiles(directory).Length == 2,
            "atomic write recovers after a cleanup failure and retains the correct backup");
    }

    private static void TestAtomicFileWrites(string folder)
    {
        string directory = Path.Combine(folder, "concurrent-writes");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "value.bin");
        File.WriteAllBytes(path, new byte[] { 0 });
        using (var firstReady = new ManualResetEventSlim())
        using (var secondReady = new ManualResetEventSlim())
        using (var releaseFirst = new ManualResetEventSlim())
        using (var releaseSecond = new ManualResetEventSlim()) {
            Action<byte, ManualResetEventSlim, ManualResetEventSlim> pendingWrite = (value, ready, release) =>
                AtomicFile.Write(path, stream => {
                    stream.WriteByte(value);
                    ready.Set();
                    if (!release.Wait(5000)) throw new TimeoutException("Atomic write test did not release the writer.");
                }, keepBackup: true);
            Task first = Task.Run(() => pendingWrite(1, firstReady, releaseFirst));
            Task second = Task.Run(() => pendingWrite(2, secondReady, releaseSecond));
            try {
                Check(firstReady.Wait(2000) && secondReady.Wait(2000), "overlapping atomic writes can prepare independent files");
                Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 0 }) && !File.Exists(path + ".bak"),
                    "uncommitted writes leave the destination and backup untouched");
                releaseFirst.Set(); first.GetAwaiter().GetResult();
                Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 1 }) && File.ReadAllBytes(path + ".bak").SequenceEqual(new byte[] { 0 }),
                    "first atomic write publishes only its own complete content");
                releaseSecond.Set(); second.GetAwaiter().GetResult();
                Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 2 }) && File.ReadAllBytes(path + ".bak").SequenceEqual(new byte[] { 1 }),
                    "later atomic write backs up the preceding published content");
            } finally {
                releaseFirst.Set(); releaseSecond.Set();
                Task.WaitAll(first, second);
            }
        }
        Throws(() => AtomicFile.Write(path, stream => { stream.WriteByte(3); throw new IOException("Injected write failure"); }, keepBackup: true),
            "atomic write propagates an interrupted writer failure");
        Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 2 }) && File.ReadAllBytes(path + ".bak").SequenceEqual(new byte[] { 1 }) && Directory.GetFiles(directory).Length == 2,
            "interrupted atomic write preserves published files and cleans up its private file");
    }
}
