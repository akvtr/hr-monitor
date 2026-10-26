using System;
using System.IO;
using System.Linq;
using System.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestRetentionObserverFailure(string folder)
    {
        string database = Path.Combine(folder, "retention-observer.db");
        using (var processed = new ManualResetEventSlim())
        using (var changed = new ManualResetEventSlim()) {
            var engine = new RecordingEngine(database, new Settings { OscEnabled = false });
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            engine.Error += errors.Enqueue;
            engine.Processed += (row, storage, sending) => processed.Set();
            engine.RetentionChanged += () => { throw new InvalidOperationException("retention observer failure"); };
            engine.RetentionChanged += changed.Set;
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 70 });
                Check(processed.Wait(2000), "retention observer test records a sample before applying finite retention");
                engine.Configure(new Settings { OscEnabled = false, RetentionDays = 1 });
                Check(changed.Wait(2000), "retention notification reaches later observers after an earlier observer fails");
                engine.Accept(new Reading { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Bpm = 80 });
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
            Check(errors.Any(message => message.Contains("retention observer failure")), "retention observer failure is reported through the error channel");
            using (var store = new Store(database))
                Check(store.Query(0, long.MaxValue).Single().Bpm == 80, "retention observer failure neither undoes deletion nor interrupts subsequent recording");
        }
    }

    private static void TestRetentionPolicy(string folder)
    {
        var config = new Settings { OscEnabled = false };
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Check(config.RetentionDays == 0 && config.RetentionCutoff(now) == null, "new settings default to unlimited storage without a deletion cutoff");
        string legacyPath = Path.Combine(folder, "legacy-retention.json");
        File.WriteAllText(legacyPath, "{\"OscEnabled\":false,\"Language\":\"en\"}");
        var legacy = Settings.Load(legacyPath);
        Check(legacy.RetentionDays == 0 && legacy.Language == "en" && !legacy.OscEnabled, "legacy settings migrate to unlimited while preserving other preferences");
        string configPath = Path.Combine(folder, "retention-settings.json");
        foreach (int days in new[] { 0, 1, 7, 30, 90, 365 }) {
            config.RetentionDays = days; config.Save(configPath);
            var loaded = Settings.Load(configPath);
            Check(loaded.RetentionDays == days && loaded.RetentionCutoff(now) == (days == 0 ? (long?)null : now - days * DayMilliseconds), "retention choice persists with correct rolling cutoff: " + days);
        }
        foreach (int days in new[] { -1, 2, int.MaxValue }) {
            var invalid = config.Copy(); invalid.RetentionDays = days;
            Throws(invalid.Validate, "unsupported retention period rejected: " + days);
        }

        config.RetentionDays = 0;
        string database = Path.Combine(folder, "retention-configurable.db");
        long ancient = now - 400 * DayMilliseconds, old = now - 40 * DayMilliseconds, middle = now - 2 * DayMilliseconds, recent = now - 3600000;
        using (var store = new Store(database)) foreach (long time in new[] { ancient, old, middle, recent })
            store.Add(new Reading { UnixMs = time, Bpm = 75, Device = "ISOLATED RETENTION TEST", Session = "retention" });
        using (var deleted = new AutoResetEvent(false))
        using (var processed = new AutoResetEvent(false)) {
            var engine = new RecordingEngine(database, config, TimeSpan.FromMilliseconds(100));
            engine.RetentionChanged += () => deleted.Set();
            engine.Processed += (row, saved, osc) => processed.Set();
            try {
                Check(engine.StartupRetentionError == null && !deleted.WaitOne(300), "unlimited storage performs no startup or scheduled deletion");
                using (var store = new Store(database)) {
                    Check(store.Query(0, long.MaxValue).Count == 4 && store.Bounds().From == ancient, "unlimited keeps recordings older than a year");
                    Check(store.Summary(ancient, ancient + 1).Count == 1 && store.Chart(ancient, recent + 1).Count == 4, "historical statistics and charts include recordings beyond 24 hours");
                    Check(store.Export(Path.Combine(folder, "long-history.csv"), ancient, recent + 1) == 4, "CSV exports old recordings under unlimited retention");
                }
                config.RetentionDays = 30; engine.Configure(config);
                Check(deleted.WaitOne(2000), "saving finite retention applies without restarting or receiving readings");
                using (var store = new Store(database)) Check(store.Query(0, long.MaxValue).Select(r => r.UnixMs).SequenceEqual(new[] { recent, middle }), "thirty-day policy deletes only recordings outside its window");
                config.RetentionDays = 1; engine.Configure(config);
                Check(deleted.WaitOne(2000), "shortening retention triggers another cleanup");
                using (var store = new Store(database)) Check(store.Query(0, long.MaxValue).Single().UnixMs == recent, "shortened retention preserves recent recordings");
                config.RetentionDays = 0; engine.Configure(config);
                engine.Accept(new Reading { UnixMs = ancient, Bpm = 76, Device = "ISOLATED RETENTION TEST", Session = "unlimited-again" });
                Check(processed.WaitOne(2000) && !deleted.WaitOne(300), "returning to unlimited disables subsequent periodic deletion");
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
        }
        var reopenedEngine = new RecordingEngine(database, new Settings { OscEnabled = false });
        reopenedEngine.StopAsync().GetAwaiter().GetResult();
        using (var store = new Store(database)) Check(store.Query(0, long.MaxValue).Count == 2 && store.Bounds().From == ancient, "unlimited restart preserves old data and does not restore previously deleted recordings");
    }

    private static void TestRetentionBoundary(string folder)
    {
        string boundaryDatabase = Path.Combine(folder, "retention-boundary.db");
        using (var store = new Store(boundaryDatabase)) {
            store.Add(new Reading { UnixMs = 999, Bpm = 70, Device = "test", Session = "retention" });
            store.Add(new Reading { UnixMs = 1000, Bpm = 71, Device = "test", Session = "retention" });
            store.Add(new Reading { UnixMs = 1001, Bpm = 72, Device = "test", Session = "retention" });
            Check(store.DeleteBefore(1000) == 1 && store.Query(0, 2000).Select(row => row.UnixMs).SequenceEqual(new long[] { 1001, 1000 }), "retention removes timestamps strictly before cutoff");
        }
    }

    private static void TestStartupRetention(string folder)
    {
        string startupRetentionDatabase = Path.Combine(folder, "retention-startup.db");
        long retentionNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long recentReading = retentionNow - DayMilliseconds + 10000;
        using (var store = new Store(startupRetentionDatabase)) {
            store.Add(new Reading { UnixMs = retentionNow - DayMilliseconds - 10000, Bpm = 70, Device = "test", Session = "old" });
            store.Add(new Reading { UnixMs = recentReading, Bpm = 72, Device = "test", Session = "recent" });
        }
        var settings = new Settings { OscEnabled = false, RetentionDays = 1 };
        var retentionEngine = new RecordingEngine(startupRetentionDatabase, settings);
        Check(retentionEngine.StartupRetentionError == null, "startup retention cleanup reports no storage error");
        retentionEngine.StopAsync().GetAwaiter().GetResult();
        using (var store = new Store(startupRetentionDatabase)) Check(store.Query(0, long.MaxValue).Select(row => row.UnixMs).SequenceEqual(new[] { recentReading }), "startup automatically removes readings older than 24 hours");
    }

    private static void TestIdleRetention(string folder)
    {
        var settings = new Settings { OscEnabled = false, RetentionDays = 1 };
        string idleRetentionDatabase = Path.Combine(folder, "retention-active-to-idle.db");
        using (var processed = new AutoResetEvent(false))
        using (var deleted = new AutoResetEvent(false)) {
            var idleEngine = new RecordingEngine(idleRetentionDatabase, settings, TimeSpan.FromMilliseconds(200));
            idleEngine.Processed += (row, saved, osc) => processed.Set();
            idleEngine.RetentionChanged += () => deleted.Set();
            try {
                // Insert after startup cleanup, then leave the queue idle before its deadline.
                idleEngine.Accept(new Reading { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - DayMilliseconds - 1000, Bpm = 70, Device = "isolated", Session = "idle-retention" });
                Check(processed.WaitOne(2000), "retention regression sample is processed before idle wait");
                Check(deleted.WaitOne(2000), "active-to-idle retention wakes at its deadline without another notification");
                using (var store = new Store(idleRetentionDatabase)) Check(store.Bounds() == null, "scheduled idle retention deletes expired rows from SQLite");
                idleEngine.Accept(new Reading { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - DayMilliseconds - 1000, Bpm = 71, Device = "isolated", Session = "next-retention" });
                Check(processed.WaitOne(2000) && deleted.WaitOne(2000), "retention continues on subsequent idle deadlines");
            } finally { idleEngine.StopAsync().GetAwaiter().GetResult(); }
        }
    }
}
