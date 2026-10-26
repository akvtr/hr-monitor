using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestCalendarEmptyState()
    {
        RunOnSta(() => {
            string language = Words.Language;
            var host = new Window();
            try {
                ThemeManager.Initialize(host); ThemeManager.Apply(host, "Dark");
                var start = new DateTime(2025, 10, 25);
                var activity = Enumerable.Range(0, 365).Select(i => new DailyActivity { Date = start.AddDays(i), Count = i == 0 ? 600 : 0 }).ToList();
                foreach (string current in new[] { "ja", "en" }) {
                    Words.Language = current;
                    ThemeManager.Apply(host, current == "ja" ? "Dark" : "Light");
                    var calendar = new ActivityCalendar(); host.Content = calendar;
                    calendar.SetDays(activity);
                    var grid = calendar.Children.OfType<Grid>().Single();
                    var header = calendar.Children.OfType<DockPanel>().First();
                    var metrics = header.Children.OfType<StackPanel>().Single(panel => panel.Orientation == Orientation.Horizontal).Children.OfType<TextBlock>().ToArray();
                    var status = header.Children.OfType<StackPanel>().Single(panel => panel.Orientation == Orientation.Vertical).Children.OfType<TextBlock>().Last();

                    calendar.SetDays(new List<DailyActivity>());
                    Check(grid.Children.Count == 0 && metrics[0].Text == Words.T("0 日記録", "0 active days") && metrics[1].Text == Words.T("0 件", "0 samples"),
                        "empty calendar clears the previous active-day and sample counts: " + current);
                    Check(status.Text == Words.T("カレンダーデータはありません", "No calendar data"), "empty calendar replaces the previous date range with a localized empty state: " + current);
                    calendar.SetDays(activity);
                    Check(grid.Children.OfType<Button>().Count() == 365 && metrics[1].Text.Contains("600"), "calendar repopulates after an empty result: " + current);
                    int rows = grid.RowDefinitions.Count, columns = grid.ColumnDefinitions.Count;
                    calendar.SetError("ISOLATED CALENDAR FAILURE");
                    Check(grid.Children.Count == 0 && grid.RowDefinitions.Count == rows && grid.ColumnDefinitions.Count == columns,
                        "calendar failure clears cells while preserving layout space: " + current);
                    Check(metrics.All(metric => metric.Text == "—") && status.Text.Contains("ISOLATED CALENDAR FAILURE"),
                        "calendar failure shows unavailable counts rather than a successful zero: " + current);
                    calendar.SetDays(new List<DailyActivity>());
                    Check(status.Text == Words.T("カレンダーデータはありません", "No calendar data") && metrics[1].Text == Words.T("0 件", "0 samples"),
                        "successful empty calendar recovery clears the previous error: " + current);
                    calendar.SetDays(activity);
                    Throws<ArgumentNullException>(() => calendar.SetDays(null), "calendar rejects a missing collection explicitly: " + current);
                    Check(grid.Children.OfType<Button>().Count() == 365 && metrics[1].Text.Contains("600"), "invalid calendar input preserves the last valid display: " + current);
                }
            } finally { Words.Language = language; host.Close(); }
        }, "Calendar empty-state verification failed");
    }
}
