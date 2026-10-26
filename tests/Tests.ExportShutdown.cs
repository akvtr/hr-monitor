using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestWindowShutdownWaitsForExport(string folder)
    {
        RunOnSta(() => {
            foreach (bool fail in new[] { false, true }) {
                string directory = Path.Combine(folder, "shutdown-export-" + fail);
                new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(Path.Combine(directory, "settings.json"));
                var window = new MainWindow(directory);
                var export = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                var processed = new ManualResetEventSlim();
                try {
                    const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                    typeof(MainWindow).GetField("historyExport", members).SetValue(window, export.Task);
                    var engine = (RecordingEngine)typeof(MainWindow).GetField("engine", members).GetValue(window);
                    var chart = (HeartChart)typeof(MainWindow).GetField("liveChart", members).GetValue(window);
                    engine.Processed += (reading, storage, sending) => processed.Set();
                    engine.Accept(new Reading { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Bpm = 70, Session = "ISOLATED EXPORT SHUTDOWN TEST" });
                    Check(processed.Wait(3000), "shutdown regression queues a UI notification before closing; failure=" + fail);
                    var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (sender, args) => closed.SetResult(true);
                    window.Close();
                    // Run a probe after queued close continuations, keeping the disk task pending.
                    var probe = window.Dispatcher.InvokeAsync(() => closed.Task.IsCompleted, DispatcherPriority.Background);
                    WaitForDispatcherTask(probe.Task);
                    Check(!probe.Task.Result, "shutdown keeps the window alive while CSV disk work is pending; failure=" + fail);
                    Check(chart.Points.Count == 0, "queued heart-rate UI notifications do not redraw the closing window; failure=" + fail);
                    if (fail) export.SetException(new IOException("ISOLATED CSV EXPORT FAILURE"));
                    else export.SetResult(1);
                    WaitForDispatcherTask(closed.Task);
                    Check(closed.Task.Result, "shutdown completes after CSV disk work settles; failure=" + fail);
                    using (var store = new Store(Path.Combine(directory, "heartrate.db")))
                        Check(store.Query(0, long.MaxValue).Count == 1, "shutdown drains accepted readings regardless of CSV success; failure=" + fail);
                    if (fail) Check(File.ReadAllText(Path.Combine(directory, "connection.log")).Contains("ISOLATED CSV EXPORT FAILURE"),
                        "shutdown reports a CSV failure instead of silently abandoning the export");
                } finally { export.TrySetResult(0); window.EndSession(); window.Close(); processed.Dispose(); }
            }
        }, "CSV shutdown verification failed");
    }
}
