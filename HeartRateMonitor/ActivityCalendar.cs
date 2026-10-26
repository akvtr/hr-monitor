using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace HeartRateMonitor
{
    public sealed class ActivityCalendar : StackPanel
    {
        private readonly TextBlock title, summary;
        private readonly Grid calendar = new Grid { Margin = new Thickness(0, 8, 0, 6) };
        private readonly List<Button> days = new List<Button>();
        private readonly Button previous, next;
        private DateTime? selectedDate;
        public DateTime Month { get; private set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        public event Action<DateTime> DaySelected;
        public event Action<DateTime> MonthChanged;

        public ActivityCalendar()
        {
            AutomationProperties.SetAutomationId(this, "ActivityCalendar");
            title = Text("", 18); title.FontWeight = FontWeights.SemiBold;
            title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            AutomationProperties.SetAutomationId(title, "ActivityMonth");
            previous = Navigation("‹", "ActivityPreviousMonth", Words.T("前の月", "Previous month"), () => SetMonth(Month.AddMonths(-1)));
            next = Navigation("›", "ActivityNextMonth", Words.T("次の月", "Next month"), () => SetMonth(Month.AddMonths(1)));
            var today = Navigation(Words.T("今月", "This month"), "ActivityThisMonth", Words.T("今月へ移動", "Go to this month"), () => SetMonth(DateTime.Today));
            today.Width = 100;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(today);
            var header = new DockPanel(); DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions); header.Children.Add(title); Children.Add(header);
            for (int i = 0; i < 7; i++) calendar.ColumnDefinitions.Add(new ColumnDefinition());
            calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            for (int i = 0; i < 6; i++) calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            AutomationProperties.SetAutomationId(calendar, "ActivityCalendarDays"); Children.Add(calendar);
            summary = Text(Words.T("読み込み中…", "Loading…"));
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            summary.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("Text") { Source = summary });
            AutomationProperties.SetAutomationId(summary, "ActivitySummary"); Children.Add(summary);
            UpdateMonthTitle();
        }

        private static Button Navigation(string text, string id, string name, Action action)
        {
            var button = new Button { Content = text, Width = 32, Height = 32, MinWidth = 0, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4, 0, 0, 0), ToolTip = name };
            AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, name);
            button.Click += (s, e) => action(); return button;
        }

        private static TextBlock Text(string text, double size = 12)
        {
            var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted"); return block;
        }

        public void SetMonth(DateTime date)
        {
            var month = new DateTime(date.Year, date.Month, 1);
            if (month > new DateTime(9999, 11, 1)) throw new ArgumentOutOfRangeException(nameof(date));
            if (month == Month) return;
            Month = month; UpdateMonthTitle(); SetLoading(); MonthChanged?.Invoke(Month);
        }

        private void UpdateMonthTitle()
        {
            title.Text = Month.ToString(Words.Language == "en" ? "MMMM yyyy" : "yyyy年 M月", Words.Language == "en" ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.InvariantCulture);
            previous.IsEnabled = Month > new DateTime(1, 1, 1);
            next.IsEnabled = Month < new DateTime(9999, 11, 1);
        }

        public void SetSelectedDate(DateTime date)
        {
            selectedDate = date.Date; UpdateSelection();
        }

        private void UpdateSelection()
        {
            foreach (var button in days) {
                bool selected = (DateTime)button.Tag == selectedDate;
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                button.SetResourceReference(Control.BorderBrushProperty, selected ? "Focus" : "Line");
                AutomationProperties.SetItemStatus(button, selected ? Words.T("選択日", "Selected day") : "");
            }
        }

        public void SetLoading()
        {
            calendar.Children.Clear(); days.Clear(); summary.Text = Words.T("読み込み中…", "Loading…");
        }

        public void SetDays(IList<DailyActivity> activity)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            var focused = days.FirstOrDefault(button => button.IsKeyboardFocused);
            DateTime? focusDate = focused == null ? (DateTime?)null : (DateTime)focused.Tag;
            calendar.Children.Clear(); days.Clear();
            var visible = activity.Where(day => day.Date.Year == Month.Year && day.Date.Month == Month.Month).ToList();
            if (visible.Count == 0) { summary.Text = Words.T("カレンダーデータはありません", "No calendar data"); return; }
            var names = Words.Language == "en" ? new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" } : new[] { "日", "月", "火", "水", "木", "金", "土" };
            for (int i = 0; i < names.Length; i++) {
                var label = Text(names[i]); label.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(label, i); calendar.Children.Add(label);
            }
            foreach (var day in visible) {
                int index = days.Count;
                string detail = day.Date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + Words.T($" · 有効な心拍 {day.Count:N0}件", $" · {day.Count:N0} valid samples");
                var button = new Button { Content = day.Date.Day.ToString(CultureInfo.InvariantCulture) + (day.Count > 0 ? " •" : ""), Tag = day.Date.Date,
                    Style = (Style)FindResource("ActivityDay"), ToolTip = detail + Words.T(" · 選ぶとその日の履歴へ", " · Select to view this day's history"), IsTabStop = false };
                button.SetResourceReference(Control.BackgroundProperty, day.Count > 0 ? "AccentSoft" : "SurfaceAlt");
                button.SetResourceReference(Control.ForegroundProperty, "Text");
                button.FontWeight = day.Count > 0 ? FontWeights.SemiBold : FontWeights.Normal;
                AutomationProperties.SetName(button, detail); AutomationProperties.SetAutomationId(button, "ActivityDay" + day.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                button.Click += (s, e) => { SetSelectedDate(day.Date); DaySelected?.Invoke(day.Date); };
                button.PreviewKeyDown += (s, e) => {
                    int offset = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : e.Key == Key.Up ? -7 : e.Key == Key.Down ? 7 : 0;
                    if (offset == 0) return;
                    int target = Math.Max(0, Math.Min(days.Count - 1, index + offset));
                    foreach (var cell in days) cell.IsTabStop = false;
                    days[target].IsTabStop = true; days[target].Focus(); e.Handled = true;
                };
                int position = (int)Month.DayOfWeek + day.Date.Day - 1;
                Grid.SetColumn(button, position % 7); Grid.SetRow(button, position / 7 + 1); calendar.Children.Add(button); days.Add(button);
            }
            var entry = days.FirstOrDefault(button => (DateTime)button.Tag == focusDate)
                ?? days.FirstOrDefault(button => (DateTime)button.Tag == selectedDate) ?? days[0];
            entry.IsTabStop = true;
            if (focusDate.HasValue) entry.Focus();
            UpdateSelection();
            long count = visible.Sum(day => day.Count); int active = visible.Count(day => day.Count > 0);
            summary.Text = Words.T($"この月: {active:N0} 日記録 · {count:N0} 件 · • 記録あり", $"This month: {active:N0} active days · {count:N0} samples · • Recorded");
        }

        public void SetError(string message)
        {
            calendar.Children.Clear(); days.Clear(); summary.Text = Words.T("カレンダーの読込エラー: ", "Calendar read failed: ") + message;
        }
    }
}
