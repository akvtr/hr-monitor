using System;
using System.IO;
using System.Linq;
using System.Text;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestStoreTextValidation(string folder)
    {
        string database = Path.Combine(folder, "invalid-text-input.db");
        using (var store = new Store(database)) {
            var rows = new[] {
                new Reading { Id = 99, UnixMs = 1000, Bpm = 70, Session = "\uD800" },
                new Reading { Id = 99, UnixMs = 1001, Bpm = 70, Device = "\uDC00" },
                new Reading { Id = 99, UnixMs = 1002, Bpm = 70, Rr = "\uDC00\uD800" }
            };
            foreach (var row in rows)
                Throws<EncoderFallbackException>(() => store.Add(row), "SQLite rejects malformed UTF-16 metadata at timestamp " + row.UnixMs);
            Check(store.Query(0, 2000).Count == 0 && rows.All(row => row.Id == 99), "rejected text creates no partial SQLite rows and does not publish an inserted ID");
            store.Add(new Reading { UnixMs = 1500, Bpm = 75, Device = "正常なセンサー\U0001F493" });
            Check(store.Query(0, 2000).Single().Device == "正常なセンサー\U0001F493", "SQLite remains usable with valid Unicode after text encoding failures");
        }

        database = Path.Combine(folder, "invalid-text-storage.db");
        ExecuteIsolatedSql(database, "INSERT INTO readings(utc_ms,session,device,bpm,rr_ms) VALUES(1000,'test',CAST(X'C328' AS TEXT),70,'');");
        using (var store = new Store(database)) {
            Throws<DecoderFallbackException>(() => store.Query(0, 2000), "SQLite reports malformed stored UTF-8 instead of returning replacement metadata");
            string csv = Path.Combine(folder, "invalid-text-export.csv");
            byte[] original = Encoding.UTF8.GetBytes("EXISTING ISOLATED CSV");
            File.WriteAllBytes(csv, original);
            Throws<DecoderFallbackException>(() => store.Export(csv, 0, 2000), "CSV reports malformed stored text rather than publishing altered metadata");
            Check(File.ReadAllBytes(csv).SequenceEqual(original), "malformed stored text preserves the existing CSV destination byte for byte");
        }

        database = Path.Combine(folder, "invalid-text-recording.db");
        var engine = new RecordingEngine(database, new Settings { OscEnabled = false });
        string failure = null;
        int processed = 0;
        engine.Error += message => failure = message;
        engine.Processed += (row, saved, sending) => processed++;
        try {
            engine.Accept(new Reading { UnixMs = 1000, Bpm = 70, Session = "\uD800" });
            engine.Accept(new Reading { UnixMs = 2000, Bpm = 75, Session = "ISOLATED VALID TEXT" });
        } finally { engine.StopAsync().GetAwaiter().GetResult(); }
        Check(!string.IsNullOrEmpty(failure) && processed == 2, "recording reports invalid text and continues processing subsequent notifications");
        using (var store = new Store(database))
            Check(store.Query(0, 3000).Single().UnixMs == 2000, "recording saves the valid following notification without silently changing invalid text");
    }
}
