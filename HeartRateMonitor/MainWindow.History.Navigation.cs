using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private TextBox historyDateInput, historyTimeInput;
        private TextBlock historyJumpStatus;

        private UIElement HistoryDateControls()
        {
            var initial = DateTimeOffset.FromUnixTimeMilliseconds((historyWindow ?? CurrentHistory()).From).LocalDateTime;
            historyDateInput = Input(initial.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            historyTimeInput = Input(initial.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            historyDateInput.Margin = historyTimeInput.Margin = new Thickness(0, 4, 0, 8);
            AutomationProperties.SetAutomationId(historyDateInput, "HistoryJumpDate");
            AutomationProperties.SetAutomationId(historyTimeInput, "HistoryJumpTime");
            AutomationProperties.SetName(historyDateInput, T("移動先の日付 yyyy/MM/dd", "Target date yyyy/MM/dd"));
            AutomationProperties.SetName(historyTimeInput, T("移動先の時刻 HH:mm:ss", "Target time HH:mm:ss"));
            foreach (var input in new[] { historyDateInput, historyTimeInput }) input.KeyDown += (s, e) => {
                if (e.Key == Key.Enter) { JumpHistory(); e.Handled = true; }
            };
            var date = Stack(Label(T("日付", "Date")), historyDateInput);
            var clock = Stack(Label(T("開始時刻", "Start time")), historyTimeInput);
            var inputs = new Grid();
            inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            inputs.Children.Add(date); Grid.SetColumn(clock, 2); inputs.Children.Add(clock);
            var jump = Button(T("指定日時へ移動", "Go to date / time"), (s, e) => JumpHistory(), id: "HistoryJump");
            jump.Width = double.NaN; jump.HorizontalAlignment = HorizontalAlignment.Stretch; jump.Margin = new Thickness(0);
            jump.Height = 40; jump.Style = (Style)FindResource("ConnectionAction");
            historyJumpStatus = Label("", 12); historyJumpStatus.MinHeight = 36;
            historyJumpStatus.Margin = new Thickness(0, 8, 0, 0);
            AutomationProperties.SetAutomationId(historyJumpStatus, "HistoryJumpStatus");
            var hint = Label(T("現在の表示幅で、指定日時から表示します。", "Open the current time span starting at the requested date and time."), 13);
            hint.Margin = new Thickness(0, 0, 0, 12);
            var zone = Label(T("PCの現地時刻 · 夏時間で重なる時刻は早い方を表示", "PC local time · Repeated daylight-saving times use the earlier occurrence"), 11);
            zone.Margin = new Thickness(0, 8, 0, 0);
            return Stack(Heading(T("日時から探す", "Find by date / time")), hint, inputs, jump, zone, historyJumpStatus);
        }

        private void JumpHistory()
        {
            TimeWindow range; string error;
            if (!HistoryDateNavigation.TryWindow(historyDateInput.Text, historyTimeInput.Text, historyWidth, TimeZoneInfo.Local, out range, out error)) {
                historyJumpStatus.Text = error; return;
            }
            // Include explicitly requested empty ranges rather than moving to another date.
            calendarDay = range; historyWindow = range;
            var date = DateTimeOffset.FromUnixTimeMilliseconds(range.From).LocalDateTime;
            activityCalendar.SetMonth(date); activityCalendar.SetSelectedDate(date);
            ScheduleHistory();
            historyJumpStatus.Text = T("指定日時から表示しています。", "Showing the range from the requested date and time.");
        }
    }
}
