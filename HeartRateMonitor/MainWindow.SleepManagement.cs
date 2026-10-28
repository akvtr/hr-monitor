using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private ComboBox sleepRangeChoice, sleepBoundaryChoice;
        private TextBox sleepFromDate, sleepToDate;
        private TextBlock sleepAverageHeart, sleepAverageDuration, sleepTypicalTime, sleepSampleNotice, sleepStatus, sleepDetail, sleepManagementConnection, sleepRhythmRange;
        private Grid sleepPage;
        private Border sleepRhythmCard, sleepDetailCard;
        private Button sleepRhythmPrevious, sleepRhythmNext;
        private SleepRhythmChart sleepRhythm;
        private HeartChart sleepDetailChart;
        private SleepAnalysis sleepAnalysis;
        private DateTime? sleepCustomFrom, sleepCustomTo;
        private DateTime sleepRangeFirstDay, sleepRangeLastDay;
        private int sleepRangeIndex, sleepManagementRevision, sleepDetailRevision, sleepRhythmPageOffset;
        private long? sleepSelectedId;
        private bool sleepManagementLoading, sleepManagementDirty = true;
        private DateTime sleepManagementRefreshAt = DateTime.MinValue, sleepManagementNextMinute = DateTime.MinValue;

        private UIElement SleepManagementView()
        {
            sleepManagementLoading = false;
            sleepRangeChoice = Choice(new[] { T("7日間", "7 days"), T("30日間", "30 days"), T("期間指定", "Custom range") }, sleepRangeIndex, 130);
            sleepRangeChoice.Height = 36;
            AutomationProperties.SetAutomationId(sleepRangeChoice, "SleepRangeChoice");
            AutomationProperties.SetName(sleepRangeChoice, T("睡眠の集計期間", "Sleep summary period"));
            var today = DateTime.Now.AddHours(-settings.SleepDayBoundaryHour).Date;
            sleepFromDate = Input((sleepCustomFrom ?? today.AddDays(-6)).ToString("yyyy/MM/dd"));
            sleepToDate = Input((sleepCustomTo ?? today).ToString("yyyy/MM/dd"));
            sleepFromDate.Width = sleepToDate.Width = 126; sleepFromDate.Height = sleepToDate.Height = 36;
            sleepFromDate.Margin = sleepToDate.Margin = new Thickness(0);
            AutomationProperties.SetAutomationId(sleepFromDate, "SleepFromDate"); AutomationProperties.SetAutomationId(sleepToDate, "SleepToDate");
            AutomationProperties.SetName(sleepFromDate, T("集計開始日 yyyy/MM/dd", "First day yyyy/MM/dd"));
            AutomationProperties.SetName(sleepToDate, T("集計終了日 yyyy/MM/dd", "Last day yyyy/MM/dd"));
            sleepFromDate.ToolTip = T("集計開始日 yyyy/MM/dd", "First day yyyy/MM/dd"); sleepToDate.ToolTip = T("集計終了日 yyyy/MM/dd", "Last day yyyy/MM/dd");
            var apply = Button(T("表示", "Apply"), async (s, e) => await ApplySleepRange(), id: "SleepApplyRange", width: 70);
            apply.Height = 36; apply.Margin = new Thickness(8, 0, 0, 0); apply.Style = (Style)FindResource("ConnectionAction");
            var custom = Horizontal(sleepFromDate, Label(" – ", 12), sleepToDate, apply);
            custom.Margin = new Thickness(8, 0, 0, 0); custom.VerticalAlignment = VerticalAlignment.Center;
            custom.Visibility = sleepRangeIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            sleepRangeChoice.SelectionChanged += async (s, e) => {
                sleepRangeIndex = sleepRangeChoice.SelectedIndex;
                custom.Visibility = sleepRangeIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
                if (sleepRangeIndex != 2 || sleepCustomFrom.HasValue) await RefreshSleepManagement();
            };
            var filters = Horizontal(sleepRangeChoice, custom); filters.VerticalAlignment = VerticalAlignment.Center;
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(filters, Dock.Right); header.Children.Add(filters);
            var title = Heading(T("睡眠", "Sleep")); title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0, 0, 12, 0); header.Children.Add(title);
            sleepAverageHeart = SleepMetric("SleepAverageHeart"); sleepAverageDuration = SleepMetric("SleepAverageDuration"); sleepTypicalTime = SleepMetric("SleepTypicalTime");
            var metrics = new Grid();
            for (int i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
            string[] captions = { T("睡眠時平均心拍数", "Average sleeping heart rate"), T("平均睡眠時間", "Average sleep duration"), T("普段の睡眠時間帯", "Typical sleep hours") };
            var values = new[] { sleepAverageHeart, sleepAverageDuration, sleepTypicalTime };
            for (int i = 0; i < values.Length; i++) {
                var cell = Stack(Label(captions[i], 12), values[i]); cell.Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0);
                Grid.SetColumn(cell, i); metrics.Children.Add(cell);
            }
            sleepSampleNotice = Label("", 11); sleepSampleNotice.Margin = new Thickness(0, 4, 0, 0);
            var overview = Card(Stack(metrics, sleepSampleNotice), new Thickness(14, 10, 14, 10)); overview.Margin = new Thickness(0, 0, 0, 8);
            sleepRhythm = new SleepRhythmChart();
            sleepRhythm.IntervalSelected += async id => { sleepSelectedId = id; await ShowSleepDetail(); };
            sleepRhythmPrevious = Button("←", async (s, e) => await ChangeSleepRhythmPage(-1), id: "SleepRhythmPrevious", width: 36);
            sleepRhythmNext = Button("→", async (s, e) => await ChangeSleepRhythmPage(1), id: "SleepRhythmNext", width: 36);
            foreach (var action in new[] { sleepRhythmPrevious, sleepRhythmNext }) { action.Height = 28; action.Margin = new Thickness(6, 0, 0, 0); action.Style = (Style)FindResource("ConnectionAction"); }
            sleepRhythmPrevious.ToolTip = T("前の7日間", "Previous 7 days"); sleepRhythmNext.ToolTip = T("次の7日間", "Next 7 days");
            AutomationProperties.SetName(sleepRhythmPrevious, T("前の7日間", "Previous 7 days")); AutomationProperties.SetName(sleepRhythmNext, T("次の7日間", "Next 7 days"));
            sleepRhythmRange = Label("", 11); sleepRhythmRange.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetAutomationId(sleepRhythmRange, "SleepRhythmRange");
            var rhythmHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var paging = Horizontal(sleepRhythmRange, sleepRhythmPrevious, sleepRhythmNext); DockPanel.SetDock(paging, Dock.Right); rhythmHeader.Children.Add(paging);
            rhythmHeader.Children.Add(Label(T("睡眠リズム", "Sleep rhythm"), 14, "Text"));
            var rhythmGrid = new Grid(); rhythmGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); rhythmGrid.RowDefinitions.Add(new RowDefinition());
            rhythmGrid.Children.Add(rhythmHeader); Grid.SetRow(sleepRhythm, 1); rhythmGrid.Children.Add(sleepRhythm);
            sleepRhythmCard = Card(rhythmGrid, new Thickness(14, 8, 14, 8)); sleepRhythmCard.Margin = new Thickness(0, 0, 0, 8);
            sleepDetail = Label(T("上の睡眠区間を選択してください。", "Select a sleep interval above."), 12, "Text");
            AutomationProperties.SetAutomationId(sleepDetail, "SleepDetail"); sleepDetail.Margin = new Thickness(0, 0, 0, 4);
            sleepDetailChart = new HeartChart { MinHeight = 120, ShowDateLabels = false, ShowMidnightSeparators = true };
            AutomationProperties.SetAutomationId(sleepDetailChart, "SleepDetailChart");
            AutomationProperties.SetName(sleepDetailChart, T("選択した睡眠区間の心拍グラフ", "Heart-rate chart for the selected sleep interval"));
            var detailGrid = new Grid(); detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detailGrid.RowDefinitions.Add(new RowDefinition());
            detailGrid.Children.Add(sleepDetail); Grid.SetRow(sleepDetailChart, 1); detailGrid.Children.Add(sleepDetailChart);
            sleepDetailCard = Card(detailGrid, new Thickness(14, 10, 14, 8));
            sleepStatus = Label("", 11); AutomationProperties.SetAutomationId(sleepStatus, "SleepStatus");
            AutomationProperties.SetLiveSetting(sleepStatus, AutomationLiveSetting.Polite); sleepStatus.Margin = new Thickness(0, 4, 0, 0);
            sleepManagementConnection = Label("", 11); sleepManagementConnection.Visibility = Visibility.Collapsed;
            sleepManagementConnection.Margin = new Thickness(0, 4, 0, 0);
            var footer = new Grid(); footer.Children.Add(sleepStatus); footer.Children.Add(sleepManagementConnection);
            sleepPage = new Grid();
            sleepPage.ToolTip = T("OyasumiVRの睡眠モードをもとに集計。空白の時間は睡眠に含めません。", "Based on OyasumiVR sleep mode. Gaps are not counted as sleep.");
            sleepPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sleepPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sleepPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 194 });
            sleepPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 162 });
            sleepPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sections = new UIElement[] { header, overview, sleepRhythmCard, sleepDetailCard, footer };
            for (int i = 0; i < sections.Length; i++) { Grid.SetRow(sections[i], i); sleepPage.Children.Add(sections[i]); }
            return sleepPage;
        }

        private static TextBlock SleepMetric(string id)
        {
            var text = Label("—", 24, "Text"); text.Margin = new Thickness(0, 3, 0, 0); text.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetAutomationId(text, id); return text;
        }

        private async Task ApplySleepRange()
        {
            DateTime from, to;
            if (!DateTime.TryParseExact(sleepFromDate.Text.Trim(), new[] { "yyyy/MM/dd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out from) ||
                !DateTime.TryParseExact(sleepToDate.Text.Trim(), new[] { "yyyy/MM/dd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out to) || to < from || (to - from).TotalDays > 365 || to == DateTime.MaxValue.Date) {
                sleepStatus.Text = T("日付を yyyy/MM/dd 形式で指定し、開始日から366日以内を選んでください。", "Enter dates as yyyy/MM/dd and choose up to 366 days in order."); return;
            }
            sleepCustomFrom = from; sleepCustomTo = to; await RefreshSleepManagement();
        }

        private async Task RefreshSleepManagement()
        {
            if (sleepRhythm == null || closing) return;
            int revision = ++sleepManagementRevision; sleepManagementLoading = true;
            sleepManagementDirty = false; int boundary = settings.SleepDayBoundaryHour;
            var last = DateTime.Now.AddHours(-boundary).Date;
            var first = last.AddDays(sleepRangeIndex == 1 ? -29 : -6);
            if (sleepRangeIndex == 2) { first = sleepCustomFrom ?? first; last = sleepCustomTo ?? last; }
            sleepStatus.Text = T("読み込み中…", "Loading…");
            try {
                var result = await Task.Run(() => { using (var store = new Store(database)) return store.LoadSleepAnalysis(first, last, TimeZoneInfo.Local, boundary); });
                if (closing || revision != sleepManagementRevision) return;
                sleepAnalysis = result;
                if (sleepRangeFirstDay != first || sleepRangeLastDay != last) sleepRhythmPageOffset = 0;
                sleepRangeFirstDay = first; sleepRangeLastDay = last;
                sleepAverageHeart.Text = result.AverageHeartRate.HasValue ? result.AverageHeartRate.Value.ToString("0.0") + " bpm" : "—";
                sleepAverageDuration.Text = result.AverageDurationMilliseconds.HasValue ? SleepDuration(result.AverageDurationMilliseconds.Value) : "—";
                sleepTypicalTime.Text = result.TypicalStartMinutes.HasValue && result.TypicalEndMinutes.HasValue ? SleepClock(result.TypicalStartMinutes.Value, boundary) + " – " + SleepClock(result.TypicalEndMinutes.Value, boundary) : "—";
                sleepSampleNotice.Text = T($"睡眠 {result.RecordedDays}日分 · 心拍 {result.HeartRateDays}日分 · 区切り {boundary:00}:00", $"Sleep: {result.RecordedDays} days · Heart rate: {result.HeartRateDays} days · Day starts at {boundary:00}:00");
                sleepSampleNotice.ToolTip = T("記録中・除外済みは平均に含めません。心拍数は有効な実測値の平均、時間帯は一日で最も長い記録の開始・終了の中央値です。", "Ongoing and excluded records are omitted from averages. Heart rate is the mean of valid samples; typical hours are medians for the longest record each day.");
                await ShowSleepRhythmPage();
                if (revision != sleepManagementRevision) return;
                sleepStatus.Text = result.Records.Count == 0 ? T("この期間に睡眠記録はありません。設定のOyasumiVR連携を確認してください。", "No sleep records in this period. Check OyasumiVR integration in Settings.") :
                    T("睡眠区間をクリック／矢印キーで選択", "Select a sleep interval by clicking or using arrow keys") + " · " + RetentionNotice(settings.RetentionDays);
                sleepStatus.ToolTip = sleepManagementConnection.Text;
                sleepManagementRefreshAt = DateTime.UtcNow.AddSeconds(10); sleepManagementNextMinute = DateTime.UtcNow.AddMinutes(1);
            } catch (Exception ex) {
                if (revision == sleepManagementRevision && !closing) { sleepStatus.Text = T("睡眠記録を読み込めません: ", "Could not load sleep records: ") + ex.Message; sleepManagementRefreshAt = DateTime.UtcNow.AddSeconds(15); sleepManagementNextMinute = sleepManagementRefreshAt; }
            } finally { if (revision == sleepManagementRevision) sleepManagementLoading = false; }
        }

        private async Task ChangeSleepRhythmPage(int direction)
        {
            if (sleepAnalysis == null) return;
            int maximum = (int)(sleepRangeLastDay - sleepRangeFirstDay).TotalDays / 7 * 7;
            sleepRhythmPageOffset = Math.Max(0, Math.Min(maximum, sleepRhythmPageOffset - direction * 7));
            await ShowSleepRhythmPage();
        }

        private async Task ShowSleepRhythmPage()
        {
            var last = sleepRangeLastDay.AddDays(-sleepRhythmPageOffset);
            var first = last.AddDays(-Math.Min(6, (last - sleepRangeFirstDay).Days));
            int boundary = settings.SleepDayBoundaryHour;
            long from = Store.SleepDayBoundary(first, TimeZoneInfo.Local, boundary), to = Store.SleepDayBoundary(last.AddDays(1), TimeZoneInfo.Local, boundary);
            var visible = sleepAnalysis.Records.Where(r => r.Interval.From < to && r.Interval.To > from).OrderByDescending(r => r.Interval.From).ToList();
            if (!visible.Any(r => r.Interval.Id == sleepSelectedId)) sleepSelectedId = visible.FirstOrDefault()?.Interval.Id;
            sleepRhythm.SelectedId = sleepSelectedId;
            sleepRhythm.SetData(visible.Select(r => r.Interval).ToList(), first, last, boundary, TimeZoneInfo.Local);
            sleepRhythmRange.Text = first.ToString("MM/dd") + " – " + last.ToString("MM/dd");
            sleepRhythmPrevious.IsEnabled = first > sleepRangeFirstDay; sleepRhythmNext.IsEnabled = last < sleepRangeLastDay;
            await ShowSleepDetail();
        }

        private async Task ShowSleepDetail()
        {
            int revision = ++sleepDetailRevision;
            var record = sleepAnalysis?.Records.FirstOrDefault(r => r.Interval.Id == sleepSelectedId);
            sleepRhythm.SelectedId = record?.Interval.Id; sleepRhythm.Refresh();
            sleepDetailChart.Points = new System.Collections.Generic.List<ChartPoint>(); sleepDetailChart.Average = null;
            sleepDetailChart.From = record?.Interval.From ?? Store.SleepDayBoundary(sleepRhythm.FirstDay, TimeZoneInfo.Local, settings.SleepDayBoundaryHour);
            sleepDetailChart.To = record?.Interval.To ?? Store.SleepDayBoundary(sleepRhythm.LastDay.AddDays(1), TimeZoneInfo.Local, settings.SleepDayBoundaryHour);
            sleepDetailChart.InvalidateVisual();
            if (record == null) { sleepDetail.Text = T("上の睡眠区間を選択してください。", "Select a sleep interval above."); return; }
            var interval = record.Interval;
            string time = LocalTime(interval.From) + " → " + LocalTime(interval.To);
            sleepDetail.Text = time + " · " + (record.Summary.Count > 0 ? T($"平均 {record.Summary.Average:0.0} bpm", $"Avg {record.Summary.Average:0.0} bpm") : T("心拍データなし", "No heart-rate data")) +
                (interval.IsOngoing ? T(" · 記録中", " · Recording") : "");
            sleepDetail.ToolTip = time + " · " + SleepDuration(record.DurationMilliseconds) + (record.Summary.Count > 0 ? T($" · 最小 {record.Summary.Min} · 最大 {record.Summary.Max} bpm · {record.Summary.Count:N0}件", $" · Min {record.Summary.Min} · Max {record.Summary.Max} bpm · {record.Summary.Count:N0} samples") : "");
            try {
                var chart = await Task.Run(() => history.LoadWindow(new TimeWindow(interval.From, interval.To)));
                if (revision != sleepDetailRevision || closing) return;
                sleepDetailChart.Points = chart.Points; sleepDetailChart.Average = record.Summary.Count > 0 ? (double?)record.Summary.Average : null;
                sleepDetailChart.GapMs = Math.Max(15000, (interval.To - interval.From) / 400 * 2); sleepDetailChart.InvalidateVisual();
            } catch (Exception ex) { if (revision == sleepDetailRevision && !closing) sleepDetail.Text = T("心拍グラフを読み込めません: ", "Could not load heart-rate chart: ") + ex.Message; }
        }

        private static string SleepDuration(double milliseconds)
        {
            var span = TimeSpan.FromMilliseconds(milliseconds);
            return T($"{(int)span.TotalHours}時間{span.Minutes:00}分", $"{(int)span.TotalHours}h {span.Minutes:00}m");
        }
        private static string SleepClock(double minutes, int boundary) => DateTime.Today.AddMinutes((minutes + boundary * 60) % 1440).ToString("HH:mm");
    }
}
