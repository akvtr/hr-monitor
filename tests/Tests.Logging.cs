using System;
using System.IO;
using System.Linq;
using System.Text;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestConnectionLog(string folder)
    {
        string directory = Path.Combine(folder, "connection-log");
        Directory.CreateDirectory(directory);
        var log = new ConnectionLog(directory);
        log.Add("before update");
        using (var reader = new StreamReader(new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), Encoding.UTF8)) {
            log.Add("after update");
            Check(log.WriteError == null && File.ReadAllText(log.Path).Contains("after update"), "connection log updates atomically while a reader holds the previous file");
            string previous = reader.ReadToEnd();
            Check(previous.Contains("before update") && !previous.Contains("after update"), "open connection log readers retain the complete previous snapshot");
        }
        byte[] saved = File.ReadAllBytes(log.Path);
        using (var locked = new FileStream(log.Path, FileMode.Open, FileAccess.Read, FileShare.None)) {
            log.Add("retained after failed write");
            Check(log.WriteError != null && log.Snapshot().Contains("retained after failed write"), "failed connection log write preserves its in-memory entry and reports an error");
        }
        Check(File.ReadAllBytes(log.Path).SequenceEqual(saved), "failed connection log replacement preserves the previous file");
        log.Add("recovered");
        Check(log.WriteError == null && File.ReadAllText(log.Path).Contains("retained after failed write") && File.ReadAllText(log.Path).Contains("recovered"),
            "connection log recovers by writing all retained entries after a storage failure");
        Check(Directory.GetFiles(directory).Length == 1, "connection log writes leave no backup or temporary files");
        log.Add("one\r\ntwo\nthree");
        log.Add(null);
        Check(log.Snapshot().Contains("one  two three") && File.ReadAllLines(log.Path).Length == 6, "log messages sanitize line breaks and accept null without adding extra entries");

        var bounded = new ConnectionLog(Path.Combine(folder, "missing-log-folder"));
        string language = Words.Language;
        try {
            for (int i = 0; i < 251; i++) bounded.Add("entry-" + i.ToString("D3"));
            Words.Language = "en";
            string snapshot = bounded.Snapshot();
            Check(!snapshot.Contains("entry-000") && snapshot.Contains("entry-001") && snapshot.Contains("entry-250"), "connection log keeps exactly the newest 250 entries even when storage is unavailable");
            Check(snapshot.Contains("Could not save log file:"), "English log snapshot localizes the storage error label");
            Directory.CreateDirectory(Path.GetDirectoryName(bounded.Path));
            bounded.Add("restored directory");
            Check(bounded.WriteError == null && File.ReadAllLines(bounded.Path).Length == 250 && !bounded.Snapshot().Contains("Could not save log file:"),
                "connection log restores bounded persistence and clears the error after directory recovery");
        } finally { Words.Language = language; }
    }
}
