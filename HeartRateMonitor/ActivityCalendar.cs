using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace HeartRateMonitor
{
    public sealed class DailyActivity
    {
        public DateTime Date;
        public long Count;
        // Convert each midnight separately so daylight-saving days need not be 24 hours.
        public static long Midnight(DateTime date, TimeZoneInfo zone)
        {
            var local = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUnixTimeMilliseconds();
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)).ToUnixTimeMilliseconds();
        }
        public static int Level(long count) => count <= 0 ? 0 : count < 600 ? 1 : count < 3600 ? 2 : count < 10800 ? 3 : 4;
    }

    public sealed class ActivityCalendar : StackPanel
    {
        private readonly TextBlock summary;
        private readonly TextBlock activeDays, sampleCount;
        private readonly Grid calendar = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        private readonly List<Button> days = new List<Button>();
        public event Action<DateTime> DaySelected;
        public ActivityCalendar()
        {
            AutomationProperties.SetAutomationId(this, "ActivityCalendar");
            var heading = new TextBlock { Text = Words.T("記録のリズム", "Recording rhythm"), FontSize = 18, FontWeight = FontWeights.SemiBold };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            summary = Text(Words.T("読み込み中…", "Loading…"));
            AutomationProperties.SetAutomationId(summary, "ActivitySummary");
            var title = new StackPanel(); title.Children.Add(heading); title.Children.Add(summary);
            activeDays = Metric(); sampleCount = Metric();
            var metrics = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            metrics.Children.Add(activeDays);
            var separator = new Border { Width = 1, Height = 28, Margin = new Thickness(20, 0, 20, 0) };
            separator.SetResourceReference(Border.BackgroundProperty, "Line"); metrics.Children.Add(separator); metrics.Children.Add(sampleCount);
            var header = new DockPanel(); DockPanel.SetDock(metrics, Dock.Right); header.Children.Add(metrics); header.Children.Add(title); Children.Add(header);
            AutomationProperties.SetAutomationId(calendar, "ActivityCalendarDays");
            Children.Add(calendar);
            var footer = new DockPanel();
            var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var thresholds = new[] { "0", "1+", "600+", "3,600+", "10,800+" };
            for (int i = 0; i <= 4; i++) {
                var mark = new Border { Width = 12, Height = 12, Margin = new Thickness(i == 0 ? 0 : 12, 0, 5, 0), CornerRadius = new CornerRadius(2) };
                mark.SetResourceReference(Border.BackgroundProperty, "Activity" + i); legend.Children.Add(mark); legend.Children.Add(Text(thresholds[i]));
            }
            legend.ToolTip = Words.T("有効な心拍の件数: 0 / 1〜599 / 600〜3,599 / 3,600〜10,799 / 10,800以上", "Valid samples: 0 / 1–599 / 600–3,599 / 3,600–10,799 / 10,800+");
            DockPanel.SetDock(legend, Dock.Right); footer.Children.Add(legend);
            footer.Children.Add(Text(Words.T("1マス = 1日 · 色の濃さは記録件数", "One cell = one day · shading shows sample count")));
            Children.Add(footer);
        }
        private static TextBlock Metric()
        {
            var block = new TextBlock { FontSize = 20, FontFamily = new System.Windows.Media.FontFamily("Bahnschrift, Segoe UI"), VerticalAlignment = VerticalAlignment.Center };
            block.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); return block;
        }
        private static TextBlock Text(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted"); return block;
        }
        public void SetDays(IList<DailyActivity> activity)
        {
            calendar.Children.Clear(); calendar.RowDefinitions.Clear(); calendar.ColumnDefinitions.Clear(); days.Clear();
            if (activity.Count == 0) return;
            long total = activity.Sum(day => day.Count); int active = activity.Count(day => day.Count > 0);
            summary.Text = Words.T($"過去365日 · {activity[0].Date:yyyy/MM/dd} — {activity[activity.Count - 1].Date:yyyy/MM/dd}", $"Past 365 days · {activity[0].Date:yyyy/MM/dd} — {activity[activity.Count - 1].Date:yyyy/MM/dd}");
            activeDays.Text = Words.T($"{active:N0} 日記録", $"{active:N0} active days"); sampleCount.Text = Words.T($"{total:N0} 件", $"{total:N0} samples");
            activeDays.ToolTip = Words.T("有効な心拍が記録された日数", "Days with valid heart-rate samples");
            sampleCount.ToolTip = Words.T("この期間に記録された有効な心拍の合計件数", "Total valid heart-rate samples in this period");
            var origin = activity[0].Date.AddDays(-(int)activity[0].Date.DayOfWeek);
            int weeks = ((activity[activity.Count - 1].Date - origin).Days / 7) + 1;
            calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            for (int i = 0; i < weeks; i++) calendar.ColumnDefinitions.Add(new ColumnDefinition());
            calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            for (int i = 0; i < 7; i++) calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) });
            foreach (int row in new[] { 1, 3, 5 }) {
                var label = Text(Words.T(new[] { "日", "月", "火", "水", "木", "金", "土" }[row], new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[row]));
                Grid.SetRow(label, row + 1); calendar.Children.Add(label);
            }
            int previousMonth = -1, lastLabelColumn = -4;
            for (int index = 0; index < activity.Count; index++) {
                var day = activity[index]; int column = (day.Date - origin).Days / 7 + 1;
                if (day.Date.Month != previousMonth) {
                    previousMonth = day.Date.Month;
                    bool shortFirstMonth = index == 0 && day.Date.AddDays(21).Month != day.Date.Month;
                    if (!shortFirstMonth && column - lastLabelColumn >= 3) {
                        var month = Text(Words.T(day.Date.Month + "月", day.Date.ToString("MMM", System.Globalization.CultureInfo.GetCultureInfo("en-US"))));
                        Grid.SetColumn(month, column); Grid.SetColumnSpan(month, Math.Min(3, weeks - column + 1)); calendar.Children.Add(month); lastLabelColumn = column;
                    }
                }
                string detail = Words.T($"{day.Date:yyyy/MM/dd} · 有効な心拍 {day.Count:N0}件", $"{day.Date:yyyy/MM/dd} · {day.Count:N0} valid samples");
                var button = new Button { Style = (Style)FindResource("ActivityDay"), ToolTip = detail + Words.T(" · 選ぶとその日の履歴へ", " · Select to view this day's history"), IsTabStop = index == activity.Count - 1 };
                button.SetResourceReference(Control.BackgroundProperty, "Activity" + DailyActivity.Level(day.Count));
                AutomationProperties.SetName(button, detail); AutomationProperties.SetAutomationId(button, "ActivityDay" + day.Date.ToString("yyyyMMdd"));
                button.Click += (s, e) => DaySelected?.Invoke(day.Date);
                int position = index;
                button.PreviewKeyDown += (s, e) => {
                    int offset = e.Key == Key.Left ? -7 : e.Key == Key.Right ? 7 : e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
                    if (offset == 0) return;
                    int target = Math.Max(0, Math.Min(days.Count - 1, position + offset));
                    foreach (var cell in days) cell.IsTabStop = false;
                    days[target].IsTabStop = true; days[target].Focus(); e.Handled = true;
                };
                Grid.SetColumn(button, column); Grid.SetRow(button, (int)day.Date.DayOfWeek + 1); calendar.Children.Add(button); days.Add(button);
            }
        }
        public void SetError(string message) { summary.Text = Words.T("カレンダーの読込エラー: ", "Calendar read failed: ") + message; activeDays.Text = "—"; sampleCount.Text = "—"; calendar.Children.Clear(); }
    }
}
