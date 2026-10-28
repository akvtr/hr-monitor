using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestWindowShutdownWaitsForSleepDeletion(string folder)
    {
        RunOnSta(() => {
            foreach (bool fail in new[] { false, true }) {
                string directory = Path.Combine(folder, "shutdown-sleep-deletion-" + fail);
                new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(Path.Combine(directory, "settings.json"));
                var window = new MainWindow(directory);
                var deletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var processed = new ManualResetEventSlim();
                try {
                    const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                    typeof(MainWindow).GetField("sleepLabelWrite", members).SetValue(window, deletion.Task);
                    var engine = (RecordingEngine)typeof(MainWindow).GetField("engine", members).GetValue(window);
                    engine.Processed += (reading, storage, sending) => processed.Set();
                    engine.Accept(new Reading { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Bpm = 73, Session = "ISOLATED SLEEP DELETE SHUTDOWN TEST" });
                    Check(processed.Wait(3000), "sleep-deletion shutdown queues a reading before closing; failure=" + fail);

                    var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (sender, args) => closed.TrySetResult(true);
                    window.Close();
                    var probe = window.Dispatcher.InvokeAsync(() => closed.Task.IsCompleted, DispatcherPriority.Background);
                    WaitForDispatcherTask(probe.Task);
                    Check(!probe.Task.Result, "shutdown waits for pending sleep-label database work; failure=" + fail);

                    if (fail) deletion.SetException(new IOException("ISOLATED SLEEP LABEL DELETE FAILURE"));
                    else deletion.SetResult(true);
                    WaitForDispatcherTask(closed.Task);
                    Check(closed.Task.Result, "shutdown completes after sleep-label work settles; failure=" + fail);
                    using (var store = new Store(Path.Combine(directory, "heartrate.db")))
                        Check(store.Query(0, long.MaxValue).Count == 1,
                            "shutdown drains accepted readings after sleep-label work succeeds or fails; failure=" + fail);
                    if (fail) Check(File.ReadAllText(Path.Combine(directory, "connection.log")).Contains("ISOLATED SLEEP LABEL DELETE FAILURE"),
                        "shutdown records a failed sleep-label operation rather than silently abandoning it");
                } finally {
                    deletion.TrySetResult(false); window.EndSession(); window.Close(); processed.Dispose();
                }
            }
        }, "Sleep deletion shutdown verification failed");
    }
}
