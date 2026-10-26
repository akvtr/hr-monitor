using System;
using System.IO;
using System.Reflection;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void ExecuteIsolatedSql(string database, string sql)
    {
        using (var store = new Store(database))
            typeof(Store).GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, new object[] { sql });
    }

    private static void TestRecordingErrorDiagnostics(string folder)
    {
        RunOnSta(() => {
            foreach (bool lockLog in new[] { false, true }) {
                string directory = Path.Combine(folder, "recording-error-log-" + lockLog);
                string database = Path.Combine(directory, "heartrate.db");
                string logPath = Path.Combine(directory, "connection.log");
                new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(Path.Combine(directory, "settings.json"));
                var window = new MainWindow(directory);
                try {
                    const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                    var engine = (RecordingEngine)typeof(MainWindow).GetField("engine", members).GetValue(window);
                    var log = (ConnectionLog)typeof(MainWindow).GetField("connectionLog", members).GetValue(window);
                    string failure = null;
                    engine.Error += message => failure = message;
                    // Break only this isolated schema to exercise the real writer failure path.
                    ExecuteIsolatedSql(database, "DROP TABLE readings;");
                    using (var held = lockLog ? new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.None) : null) {
                        engine.Accept(new Reading { UnixMs = 1000, Bpm = 70, Session = "ISOLATED ERROR REPORTING TEST" });
                        // Drain disk work without pumping the queued UI notification.
                        window.EndSession();
                        Check(!string.IsNullOrEmpty(failure), "shutdown observes the actual SQLite write failure; log locked=" + lockLog);
                        Check(log.Snapshot().Contains(failure), "recording failure reaches diagnostics before the UI callback runs; log locked=" + lockLog);
                        if (lockLog) Check(log.WriteError != null, "unavailable diagnostic storage remains observable without interrupting writer shutdown");
                        else Check(File.ReadAllText(logPath).Contains(failure), "recording failure is persisted even when shutdown discards the queued UI callback");
                    }
                } finally { window.EndSession(); window.Close(); }
            }
        }, "Recording error diagnostics verification failed");
    }

    private static void TestOscErrorDiagnostics(string folder)
    {
        RunOnSta(() => {
            string directory = Path.Combine(folder, "osc-error-log");
            new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(Path.Combine(directory, "settings.json"));
            var window = new MainWindow(directory);
            try {
                var engine = (RecordingEngine)typeof(MainWindow).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                string failure = null;
                engine.OscFailed += message => failure = message;
                var test = engine.TestAsync(70);
                window.EndSession();
                Throws<InvalidOperationException>(() => test.GetAwaiter().GetResult(), "disabled OSC test retains its send failure during shutdown");
                Check(!string.IsNullOrEmpty(failure) && File.ReadAllText(Path.Combine(directory, "connection.log")).Contains(failure),
                    "OSC failure is persisted even when shutdown discards its UI notification");
                using (var store = new Store(Path.Combine(directory, "heartrate.db")))
                    Check(store.Query(0, long.MaxValue).Count == 0, "OSC error diagnostics never create a stored reading");
            } finally { window.EndSession(); window.Close(); }
        }, "OSC error diagnostics verification failed");
    }

    private static void TestStartupErrorDiagnostics(string folder)
    {
        RunOnSta(() => {
            string directory = Path.Combine(folder, "startup-settings-error-log");
            Directory.CreateDirectory(directory);
            string settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllText(settingsPath, "{");
            var window = new MainWindow(directory);
            try {
                Check(File.ReadAllText(Path.Combine(directory, "connection.log")).Contains("Settings could not be loaded. OSC disabled."),
                    "startup settings failure is persisted independently of the initial warning label");
                Check(File.ReadAllText(settingsPath) == "{", "startup error reporting preserves the original invalid settings file");
            } finally { window.EndSession(); window.Close(); }

            directory = Path.Combine(folder, "startup-retention-error-log");
            string database = Path.Combine(directory, "heartrate.db");
            new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false, RetentionDays = 1 }.Save(Path.Combine(directory, "settings.json"));
            using (var store = new Store(database))
                store.Add(new Reading { UnixMs = 1000, Bpm = 70, Session = "ISOLATED STARTUP ERROR TEST" });
            ExecuteIsolatedSql(database, "CREATE TRIGGER reject_retention BEFORE DELETE ON readings BEGIN SELECT RAISE(ABORT,'ISOLATED STARTUP RETENTION FAILURE'); END;");
            window = new MainWindow(directory);
            try {
                Check(File.ReadAllText(Path.Combine(directory, "connection.log")).Contains("ISOLATED STARTUP RETENTION FAILURE"),
                    "startup retention failure is persisted before event subscriptions or UI dispatch");
                using (var store = new Store(database))
                    Check(store.Query(0, 2000).Count == 1, "startup diagnostics never report successful deletion after a retention failure");
            } finally { window.EndSession(); window.Close(); }
        }, "Startup error diagnostics verification failed");
    }
}
