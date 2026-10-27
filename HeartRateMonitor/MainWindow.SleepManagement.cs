using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private ComboBox sleepRangeChoice, sleepBoundaryChoice;
        private TextBox sleepFromDate, sleepToDate;
        private TextBlock sleepAverageHeart, sleepAverageDuration, sleepTypicalTime, sleepSampleNotice, sleepStatus, sleepDetail, sleepManagementConnection;
        private ListBox sleepRecords;
        private Button sleepExcludeButton, sleepDeleteButton, sleepHistoryButton;
        private SleepRhythmChart sleepRhythm;
        private HeartChart sleepDetailChart;
        private SleepAnalysis sleepAnalysis;
        private DateTime? sleepCustomFrom, sleepCustomTo;
        private int sleepRangeIndex, sleepManagementRevision, sleepDetailRevision;
        private long? sleepSelectedId;
        private bool sleepManagementLoading, sleepManagementDirty = true, sleepManagementMutating;
        private DateTime sleepManagementRefreshAt = DateTime.MinValue, sleepManagementNextMinute = DateTime.MinValue;

        private UIElement SleepManagementView()
        {
            sleepManagementLoading = false;
            sleepRangeChoice = Choice(new[] { T("7日間", "7 days"), T("30日間", "30 days"), T("期間指定", "Custom range") }, sleepRangeIndex, 150);
            AutomationProperties.SetAutomationId(sleepRangeChoice, "SleepRangeChoice");
            AutomationProperties.SetName(sleepRangeChoice, T("睡眠の集計期間", "Sleep summary period"));
            var today = DateTime.Now.AddHours(-settings.SleepDayBoundaryHour).Date;
            sleepFromDate = Input((sleepCustomFrom ?? today.AddDays(-6)).ToString("yyyy/MM/dd"));
            sleepToDate = Input((sleepCustomTo ?? today).ToString("yyyy/MM/dd"));
            sleepFromDate.Width = sleepToDate.Width = 142;
            AutomationProperties.SetAutomationId(sleepFromDate, "SleepFromDate"); AutomationProperties.SetAutomationId(sleepToDate, "SleepToDate");
            var apply = Button(T("表示", "Apply"), async (s, e) => await ApplySleepRange(), id: "SleepApplyRange", width: 86);
            var custom = Horizontal(Field(T("開始日", "First day"), sleepFromDate), new Border { Width = 8 }, Field(T("終了日", "Last day"), sleepToDate), new Border { Width = 8 }, apply);
            custom.Margin = new Thickness(12, 0, 0, 0); apply.VerticalAlignment = VerticalAlignment.Bottom; apply.Margin = new Thickness(0);
            custom.Visibility = sleepRangeIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            sleepRangeChoice.SelectionChanged += async (s, e) => {
                sleepRangeIndex = sleepRangeChoice.SelectedIndex;
                custom.Visibility = sleepRangeIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
                if (sleepRangeIndex != 2 || sleepCustomFrom.HasValue) await RefreshSleepManagement();
            };
            var filters = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) };
            filters.Children.Add(Field(T("集計期間", "Period"), sleepRangeChoice)); filters.Children.Add(custom);
            sleepAverageHeart = SleepMetric("SleepAverageHeart"); sleepAverageDuration = SleepMetric("SleepAverageDuration"); sleepTypicalTime = SleepMetric("SleepTypicalTime");
            var metrics = new Grid();
            for (int i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
            string[] captions = { T("睡眠時平均心拍数", "Average sleeping heart rate"), T("平均睡眠時間", "Average sleep duration"), T("普段の睡眠時間帯", "Typical sleep hours") };
            var values = new[] { sleepAverageHeart, sleepAverageDuration, sleepTypicalTime };
            for (int i = 0; i < values.Length; i++) {
                var cell = Stack(Label(captions[i], 12), values[i]); cell.Margin = new Thickness(i == 0 ? 0 : 16, 0, 0, 0);
                Grid.SetColumn(cell, i); metrics.Children.Add(cell);
            }
            sleepSampleNotice = Label("", 12); sleepSampleNotice.Margin = new Thickness(0, 10, 0, 0);
            var overview = Card(Stack(metrics, sleepSampleNotice), new Thickness(18));
            overview.Margin = new Thickness(0, 0, 0, 12);
            overview.SizeChanged += (s, e) => {
                bool narrow = overview.ActualWidth < 760;
                if ((metrics.RowDefinitions.Count == 3) == narrow) return;
                metrics.RowDefinitions.Clear(); metrics.ColumnDefinitions.Clear();
                for (int i = 0; i < (narrow ? 1 : 3); i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
                for (int i = 0; i < (narrow ? 3 : 1); i++) metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int i = 0; i < metrics.Children.Count; i++) {
                    var cell = (FrameworkElement)metrics.Children[i]; Grid.SetRow(cell, narrow ? i : 0); Grid.SetColumn(cell, narrow ? 0 : i);
                    cell.Margin = new Thickness(narrow || i == 0 ? 0 : 16, narrow && i != 0 ? 12 : 0, 0, 0);
                }
            };
            sleepRhythm = new SleepRhythmChart();
            AutomationProperties.SetName(sleepRhythm, T("日ごとの睡眠時間帯。下の記録一覧でも選択できます。", "Daily sleep times. Records can also be selected in the list below."));
            sleepRhythm.IntervalSelected += id => { sleepSelectedId = id; sleepRecords.SelectedItem = sleepAnalysis?.Records.FirstOrDefault(r => r.Interval.Id == id); sleepRecords.ScrollIntoView(sleepRecords.SelectedItem); };
            var rhythmScroll = new ScrollViewer { Content = sleepRhythm, MaxHeight = 236, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var rhythmCard = Card(Stack(Heading(T("睡眠リズム", "Sleep rhythm")), rhythmScroll,
                Label(T("破線：記録中／集計対象外 · 空白の時間は睡眠に含めません", "Dashed: ongoing / excluded · gaps are not counted as sleep"), 11)), new Thickness(18));
            rhythmCard.Margin = new Thickness(0, 0, 0, 12);
            sleepRecords = new ListBox { MinHeight = 110, MaxHeight = 230, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            sleepRecords.SetResourceReference(Control.BackgroundProperty, "Surface"); sleepRecords.SetResourceReference(Control.ForegroundProperty, "Text");
            AutomationProperties.SetAutomationId(sleepRecords, "SleepRecords"); AutomationProperties.SetName(sleepRecords, T("睡眠記録", "Sleep records"));
            var itemText = new FrameworkElementFactory(typeof(TextBlock));
            itemText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); itemText.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 7, 8, 7));
            itemText.SetBinding(TextBlock.TextProperty, new Binding { Converter = new SleepRecordText() });
            sleepRecords.ItemTemplate = new DataTemplate { VisualTree = itemText };
            sleepRecords.SelectionChanged += async (s, e) => await ShowSleepDetail();
            sleepDetail = Label(T("記録を選択してください。", "Select a sleep record."), 12); AutomationProperties.SetAutomationId(sleepDetail, "SleepDetail");
            sleepDetail.Margin = new Thickness(0, 10, 0, 8);
            sleepDetailChart = new HeartChart { Height = 150, MinHeight = 150, Visibility = Visibility.Collapsed, ShowDateLabels = true };
            sleepHistoryButton = Button(T("履歴で開く", "Open in History"), async (s, e) => await OpenSelectedSleepHistory(), id: "SleepOpenHistory", width: 160);
            sleepExcludeButton = Button(T("集計から除外", "Exclude from summary"), async (s, e) => await ChangeSleepExclusion(), id: "SleepExclude", width: 190);
            sleepDeleteButton = Button(T("削除", "Delete"), async (s, e) => await DeleteSelectedSleep(), id: "SleepDelete", width: 90);
            sleepDeleteButton.SetResourceReference(Control.ForegroundProperty, "Danger");
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var action in new[] { sleepHistoryButton, sleepExcludeButton, sleepDeleteButton }) { action.IsEnabled = false; action.Style = (Style)FindResource("ConnectionAction"); action.Height = 36; actions.Children.Add(action); }
            var recordsCard = Card(Stack(Heading(T("睡眠記録", "Sleep records")), sleepRecords, sleepDetail, sleepDetailChart, actions), new Thickness(18));
            sleepStatus = Label("", 12); AutomationProperties.SetAutomationId(sleepStatus, "SleepStatus");
            AutomationProperties.SetLiveSetting(sleepStatus, AutomationLiveSetting.Polite); sleepStatus.Margin = new Thickness(0, 8, 0, 8);
            sleepManagementConnection = Label("", 12);
            var content = Stack(Heading(T("睡眠管理", "Sleep management")),
                Label(T("OyasumiVRの睡眠モードをもとに集計", "Based on OyasumiVR sleep mode"), 12), sleepManagementConnection, filters, overview, rhythmCard, recordsCard, sleepStatus,
                Label(RetentionNotice(settings.RetentionDays), 12));
            content.Margin = new Thickness(0, 0, 4, 8);
            return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private static TextBlock SleepMetric(string id)
        {
            var text = Label("—", 26, "Text"); text.Margin = new Thickness(0, 6, 0, 0); text.FontWeight = FontWeights.SemiBold;
            AutomationProperties.SetAutomationId(text, id); return text;
        }

        private async Task ApplySleepRange()
        {
            DateTime from, to;
            if (!DateTime.TryParseExact(sleepFromDate.Text.Trim(), new[] { "yyyy/MM/dd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out from) ||
                !DateTime.TryParseExact(sleepToDate.Text.Trim(), new[] { "yyyy/MM/dd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out to) || to < from || (to - from).TotalDays > 365) {
                sleepStatus.Text = T("日付を yyyy/MM/dd 形式で指定し、開始日から366日以内を選んでください。", "Enter dates as yyyy/MM/dd and choose up to 366 days in order."); return;
            }
            sleepCustomFrom = from; sleepCustomTo = to; await RefreshSleepManagement();
        }

        private async Task RefreshSleepManagement()
        {
            if (sleepRecords == null || closing) return;
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
                sleepAverageHeart.Text = result.AverageHeartRate.HasValue ? result.AverageHeartRate.Value.ToString("0.0") + " bpm" : "—";
                sleepAverageDuration.Text = result.AverageDurationMilliseconds.HasValue ? SleepDuration(result.AverageDurationMilliseconds.Value) : "—";
                sleepTypicalTime.Text = result.TypicalStartMinutes.HasValue && result.TypicalEndMinutes.HasValue ? SleepClock(result.TypicalStartMinutes.Value, boundary) + " – " + SleepClock(result.TypicalEndMinutes.Value, boundary) : "—";
                sleepSampleNotice.Text = T($"睡眠 {result.RecordedDays}日分 · 心拍 {result.HeartRateDays}日分 · 一日の区切り {boundary:00}:00", $"Sleep: {result.RecordedDays} days · Heart rate: {result.HeartRateDays} days · Day starts at {boundary:00}:00");
                sleepSampleNotice.ToolTip = T("記録中・除外済みは平均に含めません。心拍数は有効な実測値の平均、時間帯は一日で最も長い記録の開始・終了の中央値です。", "Ongoing and excluded records are omitted from averages. Heart rate is the mean of valid samples; typical hours are medians for the longest record each day.");
                sleepRhythm.SetData(result.Records.Select(r => r.Interval).ToList(), first, last, boundary, TimeZoneInfo.Local);
                long? selectedId = sleepSelectedId;
                var records = result.Records.OrderByDescending(r => r.Interval.From).ToList();
                sleepRecords.ItemsSource = records;
                sleepRecords.SelectedItem = records.FirstOrDefault(r => r.Interval.Id == selectedId) ?? records.FirstOrDefault();
                await ShowSleepDetail();
                if (revision != sleepManagementRevision) return;
                sleepStatus.Text = result.Records.Count == 0 ? T("この期間に睡眠記録はありません。設定のOyasumiVR連携を確認してください。", "No sleep records in this period. Check OyasumiVR integration in Settings.") : T($"{result.Records.Count}区間 · 記録のない日は平均の対象外です。", $"{result.Records.Count} intervals · Days without records are omitted from averages.");
                sleepManagementRefreshAt = DateTime.UtcNow.AddSeconds(10); sleepManagementNextMinute = DateTime.UtcNow.AddMinutes(1);
            } catch (Exception ex) {
                if (revision == sleepManagementRevision && !closing) { sleepStatus.Text = T("睡眠記録を読み込めません: ", "Could not load sleep records: ") + ex.Message; sleepManagementRefreshAt = DateTime.UtcNow.AddSeconds(15); sleepManagementNextMinute = sleepManagementRefreshAt; }
            } finally { if (revision == sleepManagementRevision) sleepManagementLoading = false; }
        }

        private async Task ShowSleepDetail()
        {
            int revision = ++sleepDetailRevision; var record = sleepRecords.SelectedItem as SleepRecord;
            sleepSelectedId = record?.Interval.Id; sleepRhythm.SelectedId = sleepSelectedId; sleepRhythm.InvalidateVisual();
            bool editable = record != null && !record.Interval.IsOngoing && !sleepManagementMutating;
            sleepHistoryButton.IsEnabled = record != null; sleepExcludeButton.IsEnabled = sleepDeleteButton.IsEnabled = editable;
            sleepExcludeButton.Content = record != null && record.Interval.Excluded ? T("集計に戻す", "Include in summary") : T("集計から除外", "Exclude from summary");
            sleepDetailChart.Visibility = Visibility.Collapsed;
            if (record == null) { sleepDetail.Text = T("記録を選択してください。", "Select a sleep record."); return; }
            var interval = record.Interval;
            string heart = record.Summary.Count == 0 ? T("心拍データなし", "No heart-rate data") : T($"平均 {record.Summary.Average:0.0} · 最小 {record.Summary.Min} · 最大 {record.Summary.Max} bpm · 有効測定 {record.Summary.Count:N0}件", $"Avg {record.Summary.Average:0.0} · Min {record.Summary.Min} · Max {record.Summary.Max} bpm · {record.Summary.Count:N0} valid samples");
            sleepDetail.Text = LocalTime(interval.From) + " → " + LocalTime(interval.To) + "\n" + SleepDuration(record.DurationMilliseconds) + " · " + heart +
                (interval.IsOngoing ? T("\n記録中 · 終了後に集計・管理できます。", "\nRecording · Summary and management are available after it ends.") : interval.Excluded ? T("\n集計対象外 · 元の記録は保持しています。", "\nExcluded from summary · Original record retained.") : "");
            try {
                var chart = await Task.Run(() => history.LoadWindow(new TimeWindow(interval.From, interval.To)));
                if (revision != sleepDetailRevision || closing) return;
                sleepDetailChart.Points = chart.Points; sleepDetailChart.From = interval.From; sleepDetailChart.To = interval.To;
                sleepDetailChart.Average = record.Summary.Count > 0 ? (double?)record.Summary.Average : null;
                sleepDetailChart.GapMs = Math.Max(15000, (interval.To - interval.From) / 400 * 2);
                sleepDetailChart.Visibility = chart.Points.Count > 0 ? Visibility.Visible : Visibility.Collapsed; sleepDetailChart.InvalidateVisual();
            } catch (Exception ex) { if (revision == sleepDetailRevision && !closing) sleepDetail.Text += "\n" + ex.Message; }
        }

        private async Task ChangeSleepExclusion()
        {
            var record = sleepRecords.SelectedItem as SleepRecord; if (record == null || record.Interval.IsOngoing || sleepManagementMutating) return;
            await MutateSleepRecord(() => { using (var store = new Store(database)) return store.SetSleepExcluded(record.Interval.Id, !record.Interval.Excluded); });
        }

        private async Task DeleteSelectedSleep(bool confirm = true)
        {
            var record = sleepRecords.SelectedItem as SleepRecord; if (record == null || record.Interval.IsOngoing || sleepManagementMutating) return;
            if (confirm && MessageBox.Show(this, T("選択した睡眠ラベルを完全に削除します。元に戻せません。心拍記録は残ります。", "Permanently delete this sleep label? This cannot be undone. Heart-rate recordings will be kept."), T("睡眠ラベルの削除", "Delete sleep label"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await MutateSleepRecord(() => { using (var store = new Store(database)) return store.DeleteSleepInterval(record.Interval.Id); });
        }

        private async Task MutateSleepRecord(Func<bool> mutation)
        {
            sleepManagementMutating = true; sleepDeleteButton.IsEnabled = sleepExcludeButton.IsEnabled = false;
            try {
                if (!await Task.Run(mutation)) throw new InvalidOperationException(T("記録が変更されたか、記録中のため操作できません。", "The record changed or is still ongoing."));
                sleepHistoryDirty = true; await RefreshSleepManagement();
            } catch (Exception ex) { if (!closing) sleepStatus.Text = ex.Message; }
            finally { sleepManagementMutating = false; if (!closing) await ShowSleepDetail(); }
        }

        private async Task OpenSelectedSleepHistory()
        {
            var record = sleepRecords.SelectedItem as SleepRecord; if (record == null) return;
            var interval = record.Interval;
            calendarDay = new TimeWindow(interval.From, Math.Max(interval.From + 60000, interval.To));
            historyWindow = calendarDay; historyWidth = calendarDay.To - calendarDay.From;
            tabs.SelectedIndex = 1; await RefreshHistory(false);
        }

        private static string SleepDuration(double milliseconds)
        {
            var span = TimeSpan.FromMilliseconds(milliseconds);
            return T($"{(int)span.TotalHours}時間{span.Minutes:00}分", $"{(int)span.TotalHours}h {span.Minutes:00}m");
        }
        private static string SleepClock(double minutes, int boundary) => DateTime.Today.AddMinutes((minutes + boundary * 60) % 1440).ToString("HH:mm");

        private sealed class SleepRecordText : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var record = value as SleepRecord; if (record == null) return "";
                var interval = record.Interval;
                return LocalTime(interval.From) + " → " + LocalTime(interval.To) + "\n" + SleepDuration(record.DurationMilliseconds) + " · " +
                    (record.Summary.Count > 0 ? record.Summary.Average.ToString("0.0") + " bpm" : T("心拍データなし", "No heart-rate data")) +
                    (interval.IsOngoing ? T(" · 記録中", " · Recording") : interval.Excluded ? T(" · 集計対象外", " · Excluded") : "");
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }
}
