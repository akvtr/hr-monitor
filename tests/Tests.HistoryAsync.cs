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
                var initialCalendar = (ActivityCalendar)typeof(MainWindow).GetField("activityCalendar", members).GetValue(window);
                var initialRange = (TimeWindow)rangeField.GetValue(window);
                var initialDate = DateTimeOffset.FromUnixTimeMilliseconds(initialRange.From).LocalDateTime;
                Check(initialCalendar.Month == new DateTime(initialDate.Year, initialDate.Month, 1), "initial calendar opens the month of the latest recording even when all data is old");

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

                var dateInput = (System.Windows.Controls.TextBox)typeof(MainWindow).GetField("historyDateInput", members).GetValue(window);
                var timeInput = (System.Windows.Controls.TextBox)typeof(MainWindow).GetField("historyTimeInput", members).GetValue(window);
                dateInput.Text = "2020/02/29"; timeInput.Text = "21:30:15";
                typeof(MainWindow).GetMethod("JumpHistory", members).Invoke(window, null);
                var jumped = (TimeWindow)rangeField.GetValue(window);
                long requested = new DateTimeOffset(new DateTime(2020, 2, 29, 21, 30, 15)).ToUnixTimeMilliseconds();
                Check(jumped.From == requested && jumped.To == requested + 600000, "window date jump preserves an empty range more than a year before the data");
                WaitForDispatcherTask((Task)typeof(MainWindow).GetMethod("LoadHistory", members).Invoke(window, null));
                Check(((System.Windows.Controls.TextBlock)typeof(MainWindow).GetField("historyStats", members).GetValue(window)).Text.Contains(Words.T("ありません", "No valid")),
                    "empty requested date displays an empty state instead of unrelated readings");
                dateInput.Text = "2020/02/30";
                typeof(MainWindow).GetMethod("JumpHistory", members).Invoke(window, null);
                Check(ReferenceEquals(jumped, rangeField.GetValue(window)), "invalid date input leaves the current chart range unchanged");
                var calendar = (ActivityCalendar)typeof(MainWindow).GetField("activityCalendar", members).GetValue(window);
                calendar.SetMonth(new DateTime(2024, 2, 1));
                var refreshActivity = typeof(MainWindow).GetMethod("RefreshActivity", members);
                var oldMonth = (Task)refreshActivity.Invoke(window, new object[] { true });
                calendar.SetMonth(new DateTime(2023, 12, 1));
                var newMonth = (Task)refreshActivity.Invoke(window, new object[] { true });
                WaitForDispatcherTask(Task.WhenAll(oldMonth, newMonth));
                var grid = System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<System.Windows.Controls.Grid>(calendar.Children));
                var cells = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OfType<System.Windows.Controls.Button>(grid.Children));
                Check(cells.Count == 31 && cells.TrueForAll(cell => ((DateTime)cell.Tag).Month == 12), "late calendar results cannot replace the most recently requested month");
            }
            finally { if (window != null) { window.EndSession(); window.Close(); } }
        }, "History refresh navigation verification failed");
    }
}
