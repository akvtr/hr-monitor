using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestWindowShutdownWithStoppedWriter(string folder)
    {
        RunOnSta(() => {
            string directory = Path.Combine(folder, "shutdown-stopped-writer");
            new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(Path.Combine(directory, "settings.json"));
            var window = new MainWindow(directory);
            try {
                var engine = (RecordingEngine)typeof(MainWindow).GetField("engine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                engine.StopAsync().GetAwaiter().GetResult();
                var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (sender, args) => closed.SetResult(true);
                window.Close();
                WaitForDispatcherTask(closed.Task);
                Check(closed.Task.Result, "window closes on the first request when every shutdown task is already complete");
            } finally { window.EndSession(); window.Close(); }
        }, "Synchronous window shutdown verification failed");
    }

    private static void TestWindowShutdownAfterConnectionFailure(string folder)
    {
        RunOnSta(() => {
            MainWindow window = null;
            RecordingEngine engine = null;
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim()) {
                try {
                    string directory = Path.Combine(folder, "shutdown-connection-failure");
                    new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(Path.Combine(directory, "settings.json"));
                    window = new MainWindow(directory);
                    const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                    // Inject a failed discovery task without accessing Bluetooth hardware.
                    typeof(MainWindow).GetField("manualSearch", fields).SetValue(window, Task.FromException(new InvalidOperationException("ISOLATED SHUTDOWN FAILURE")));
                    engine = (RecordingEngine)typeof(MainWindow).GetField("engine", fields).GetValue(window);
                    engine.Processed += (reading, saved, osc) => { entered.Set(); release.Wait(); };
                    engine.Accept(new Reading { UnixMs = 1000, Bpm = 70, Session = "ISOLATED SHUTDOWN TEST" });
                    Check(entered.Wait(3000), "shutdown regression holds an accepted reading in the writer");
                    engine.Accept(new Reading { UnixMs = 2000, Bpm = 71, Session = "ISOLATED SHUTDOWN TEST" });
                    var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (sender, args) => closed.SetResult(true);
                    window.Close();
                    Check(!closed.Task.IsCompleted, "connection shutdown failure still waits for the recording queue before closing");
                    window.RestoreWindow();
                    Check(!window.IsVisible, "restore requests during shutdown do not reopen the draining window");
                    release.Set();
                    WaitForDispatcherTask(closed.Task);
                    Check(closed.Task.Result, "window closes after draining recordings despite a failed connection task");
                    using (var store = new Store(Path.Combine(directory, "heartrate.db")))
                        Check(store.Query(0, 3000).Count == 2, "shutdown preserves every queued reading after a connection failure");
                    Check(File.ReadAllText(Path.Combine(directory, "connection.log")).Contains("ISOLATED SHUTDOWN FAILURE"),
                        "shutdown records the connection failure while completing writer cleanup");
                    window.RestoreWindow();
                    Check(!window.IsVisible, "late restore requests leave a closed window closed without throwing");
                }
                finally {
                    release.Set();
                    if (engine != null) engine.StopAsync().GetAwaiter().GetResult();
                    if (window != null) { window.EndSession(); window.Close(); }
                }
            }
        }, "Window shutdown verification failed");
    }
}
