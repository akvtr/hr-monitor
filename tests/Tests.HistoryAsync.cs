using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestHistoryRefreshPreservesNavigation(string folder)
    {
        RunOnSta(() => {
            MainWindow window = null;
            try {
                string directory = Path.Combine(folder, "history-refresh-navigation");
                new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(Path.Combine(directory, "settings.json"));
                const long start = 1700000000000;
                using (var store = new Store(Path.Combine(directory, "heartrate.db")))
                    for (int i = 0; i < 4; i++) store.Add(new Reading { UnixMs = start + i * 3600000L, Bpm = 70 + i, Session = "ISOLATED HISTORY TEST" });
                window = new MainWindow(directory);
                const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                var refresh = typeof(MainWindow).GetMethod("RefreshHistory", members);
                var move = typeof(MainWindow).GetMethod("MoveHistoryTo", members);
                var rangeField = typeof(MainWindow).GetField("historyWindow", members);
                WaitForDispatcherTask((Task)refresh.Invoke(window, new object[] { false }));

                var pending = (Task)refresh.Invoke(window, new object[] { true });
                Check(!pending.IsCompleted, "history regression starts an asynchronous latest-window refresh");
                move.Invoke(window, new object[] { start + 600000 });
                var chosen = (TimeWindow)rangeField.GetValue(window);
                Check(chosen.From == start && chosen.To == start + 600000, "history navigation chooses an earlier window while refresh is pending");
                WaitForDispatcherTask(pending);
                var completed = (TimeWindow)rangeField.GetValue(window);
                Check(completed.From == chosen.From && completed.To == chosen.To,
                    "late latest-window results preserve navigation performed during the refresh");

                WaitForDispatcherTask((Task)refresh.Invoke(window, new object[] { true }));
                completed = (TimeWindow)rangeField.GetValue(window);
                Check(completed.To == start + 3 * 3600000L + 1 && completed.To - completed.From == 600000,
                    "latest-window refresh still selects the latest recordings when navigation has not changed");
            }
            finally { if (window != null) { window.EndSession(); window.Close(); } }
        }, "History refresh navigation verification failed");
    }
}
