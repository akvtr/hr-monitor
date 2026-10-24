using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private readonly DispatcherTimer historyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private HistoryNavigator historyNavigator;
        private TimeSpanPicker historySpanPicker;
        private readonly List<RadioButton> historyPresets = new List<RadioButton>();
        private List<ChartPoint> historyPreview = new List<ChartPoint>(), historyOverview = new List<ChartPoint>();
        private TextBlock historyStats, historyRange, historyBounds, historyFirst, historyLast, historyPosition, exportStatus;
        private Button exportButton, historyBack, historyForward;
        private TimeWindow historyData, historyWindow;
        private TimeWindow calendarDay;
        private long historyWidth = 600000;
        private int historyScaleIndex;
        private int historyRevision;
        private int historyBoundsRevision;
        private ActivityCalendar activityCalendar;
        private DateTime activityRefreshAt = DateTime.MinValue;
        private int activityRevision;
        private bool activityLoading;
        private bool settingHistory, historyTimerAttached, historyLoaded, exportingHistory, historyRefreshPending;
        private UIElement HistoryView()
        {
            activityRevision++; activityLoading = false; activityRefreshAt = DateTime.MinValue;
            activityCalendar = new ActivityCalendar();
            activityCalendar.DaySelected += date => {
                long from = DailyActivity.Midnight(date, TimeZoneInfo.Local), to = DailyActivity.Midnight(date.AddDays(1), TimeZoneInfo.Local);
                // Keep the requested day, including empty days, rather than clamping to a different date.
                calendarDay = new TimeWindow(from, to);
                historyWindow = calendarDay; historyWidth = to - from;
                historyNavigator.SetData(HistoryDomain(), historyOverview, historyWindow, historyData != null);
                ScheduleHistory();
            };
            historyStats = Label("", 12); historyRange = Label("", 26, "Text"); historyRange.FontWeight = FontWeights.SemiBold; historyRange.FontFamily = new FontFamily("Bahnschrift, Segoe UI");
            historyBounds = Label("", 12); historyFirst = Label("", 13, "Text"); historyLast = Label("", 13, "Text");
            historyPosition = Label("", 12, "Accent"); exportStatus = Label("", 12);
            foreach (var status in new[] { historyStats, exportStatus }) {
                status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis;
                status.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("Text") { Source = status });
            }
            AutomationProperties.SetAutomationId(historyRange, "HistoryVisibleRange");
            AutomationProperties.SetAutomationId(historyStats, "HistorySummary");
            var spans = new[] { T("10分", "10 min"), T("30分", "30 min"), T("1時間", "1 hour"), T("6時間", "6 hours"), T("24時間", "24 hours"), T("全体", "All") };
            historyPresets.Clear();
            var presets = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(presets, "HistoryScale");
            for (int i = 0; i < spans.Length; i++) {
                int index = i;
                var option = new RadioButton { Content = spans[i], GroupName = "HistoryTimeSpan", IsChecked = i == historyScaleIndex, Style = (Style)FindResource("TimeRangeOption") };
                AutomationProperties.SetAutomationId(option, "HistoryScale" + i);
                AutomationProperties.SetName(option, T("表示幅 ", "Time span ") + spans[i]);
                option.Checked += (s, e) => { if (settingHistory) return; long end = historyWindow?.To ?? historyData?.To ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); historyScaleIndex = index; UpdateHistoryWidth(); MoveHistoryTo(end); };
                presets.Children.Add(option); historyPresets.Add(option);
            }
            historyNavigator = new HistoryNavigator();
            historyNavigator.ZoomRequested += ZoomHistory;
            historyNavigator.InteractionStarted += () => historyDebounce.Stop();
            historyNavigator.SelectionChanged += range => { historyWindow = range; historyWidth = range.To - range.From; ScheduleHistory(); };
            historyNavigator.InteractionCompleted += () => {
                if (historyRefreshPending) { historyRefreshPending = false; _ = RefreshHistory(false); }
                else { historyDebounce.Stop(); historyDebounce.Start(); }
            };
            historyNavigator.SetData(HistoryDomain(), historyOverview, historyWindow ?? CurrentHistory(), historyData != null);
            if (!historyTimerAttached) { historyDebounce.Tick += async (s, e) => { historyDebounce.Stop(); await LoadHistory(); }; historyTimerAttached = true; }
            var latest = Button(T("最新の記録へ", "Latest recordings"), async (s, e) => await RefreshHistory(true), id: "HistoryLatest", width: 164); latest.Margin = new Thickness(0, 0, 12, 0); latest.Height = 40;
            historySpanPicker = new TimeSpanPicker("HistorySpanSlider") { Width = 300, Margin = new Thickness(16, 0, 0, 0) };
            historySpanPicker.WidthChanged += width => { historyWidth = width; MoveHistoryTo(historyWindow?.To ?? HistoryDomain().To); };
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(historySpanPicker, Dock.Right); toolbar.Children.Add(historySpanPicker); toolbar.Children.Add(presets);

            historyBack = Button(T("← 前の期間", "← Previous"), (s, e) => MoveHistoryTo((historyWindow?.To ?? 0) - historyWidth), id: "HistoryPrevious", width: 112);
            historyForward = Button(T("次の期間 →", "Next →"), (s, e) => MoveHistoryTo((historyWindow?.To ?? 0) + historyWidth), id: "HistoryNext", width: 112); historyForward.Margin = new Thickness(0);
            historyBack.Height = historyForward.Height = 40;
            historyBack.ToolTip = T("選択した表示幅だけ前へ移動", "Move back by the selected time span"); historyForward.ToolTip = T("選択した表示幅だけ次へ移動", "Move forward by the selected time span");
            var rangeRow = new DockPanel(); var arrows = Horizontal(latest, historyBack, historyForward); arrows.Margin = new Thickness(16, 0, 0, 0); DockPanel.SetDock(arrows, Dock.Right); rangeRow.Children.Add(arrows);
            rangeRow.Children.Add(Stack(historyRange, historyBounds)); historyBounds.Margin = new Thickness(0, 4, 0, 0);
            historyStats.Margin = new Thickness(0, 6, 0, 4);
            var plotGrid = new Grid(); plotGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); plotGrid.RowDefinitions.Add(new RowDefinition { MinHeight = 120 }); plotGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            plotGrid.Children.Add(Stack(toolbar, rangeRow, historyStats));
            historyChart.MinHeight = 120; AutomationProperties.SetAutomationId(historyChart, "HistoryChart"); Grid.SetRow(historyChart, 1); plotGrid.Children.Add(historyChart);

            var timelineTitle = new DockPanel(); DockPanel.SetDock(historyPosition, Dock.Right); timelineTitle.Children.Add(historyPosition); timelineTitle.Children.Add(Label(T("全体グラフ", "OVERVIEW"), 12));
            var endpoints = new Grid(); endpoints.ColumnDefinitions.Add(new ColumnDefinition()); endpoints.ColumnDefinitions.Add(new ColumnDefinition()); endpoints.Children.Add(historyFirst); Grid.SetColumn(historyLast, 1); historyLast.HorizontalAlignment = HorizontalAlignment.Right; endpoints.Children.Add(historyLast);
            var help = Label(T("Ctrl＋ホイールで拡大・縮小。下の両端で範囲変更、中央で移動。上のグラフをドラッグすると区間平均を表示（Esc・右クリックで解除）。", "Ctrl+wheel to zoom. Drag overview edges to resize, or its middle to pan. Drag the main chart for interval statistics (Escape / right-click to clear)."), 12);
            var timeline = Stack(timelineTitle, historyNavigator, endpoints, help); timeline.Margin = new Thickness(0, 6, 0, 0); help.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(timeline, 2); plotGrid.Children.Add(timeline);

            exportButton = Button(T("表示期間をCSV出力", "Export range to CSV"), ExportClicked, primary: true, id: "HistoryExport", width: 196); exportButton.Margin = new Thickness(16, 0, 0, 0); exportButton.IsEnabled = false;
            exportButton.ToolTip = T("表示期間に含まれる全測定値を書き出します。", "Exports every sample in the displayed range.");
            foreach (var action in new[] { latest, historyBack, historyForward, exportButton }) {
                action.Style = (Style)FindResource("ConnectionAction");
                action.Height = 40;
            }
            var retention = Label(RetentionNotice(settings.RetentionDays), 12); AutomationProperties.SetAutomationId(retention, "HistoryRetentionNotice");
            var footerActions = new DockPanel(); DockPanel.SetDock(exportButton, Dock.Right); footerActions.Children.Add(exportButton); footerActions.Children.Add(retention);
            exportStatus.Margin = new Thickness(0, 4, 0, 0);
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var activityCard = Card(activityCalendar, new Thickness(18)); activityCard.Margin = new Thickness(0, 0, 0, 10); grid.Children.Add(activityCard);
            var plotCard = Card(plotGrid, new Thickness(18)); Grid.SetRow(plotCard, 1); grid.Children.Add(plotCard);
            var footer = Stack(footerActions, exportStatus); footer.Margin = new Thickness(2, 8, 2, 0); Grid.SetRow(footer, 2); grid.Children.Add(footer);
            // Grow the plot in ordinary windows. When connection details, display
            // scaling or a small window leave less room, fit the entire page uniformly.
            // There is no scrolling surface, and controls are never cropped to fit.
            grid.Width = 940; grid.Height = 840;
            var fit = new Viewbox { Child = grid, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            var viewport = new Grid(); viewport.Children.Add(fit);
            AutomationProperties.SetAutomationId(viewport, "HistoryViewport");
            viewport.SizeChanged += (s, e) => { grid.Width = Math.Max(940, e.NewSize.Width); grid.Height = Math.Max(840, e.NewSize.Height); };
            return viewport;
        }
        private async Task RefreshActivity()
        {
            if (activityLoading || activityCalendar == null) return;
            activityLoading = true; int revision = ++activityRevision; var today = DateTime.Today;
            try {
                var activity = await Task.Run(() => { using (var store = new Store(database)) return store.DailyActivity(today.AddDays(-364), today, TimeZoneInfo.Local); });
                if (revision != activityRevision || closing) return;
                activityCalendar.SetDays(activity);
            } catch (Exception ex) { if (revision == activityRevision && !closing) activityCalendar.SetError(ex.Message); }
            finally { if (revision == activityRevision) { activityLoading = false; activityRefreshAt = DateTime.UtcNow.AddMinutes(1); } }
        }
        private async Task RefreshHistory(bool latestWindow)
        {
            if (latestWindow) calendarDay = null;
            _ = RefreshActivity();
            if (historyNavigator.IsInteracting) { historyRefreshPending = true; return; }
            int revision = ++historyBoundsRevision;
            try {
                var snapshot = await Task.Run(() => { using (var store = new Store(database)) {
                    var bounds = store.Bounds();
                    return Tuple.Create(bounds, bounds == null ? new List<ChartPoint>() : store.Chart(bounds.From, bounds.To, 14400),
                        bounds == null ? new List<ChartPoint>() : store.Chart(bounds.From, bounds.To, 1200));
                } });
                if (revision != historyBoundsRevision || closing) return;
                if (historyNavigator.IsInteracting) { historyRefreshPending = true; return; }
                historyData = snapshot.Item1; historyPreview = snapshot.Item2; historyOverview = snapshot.Item3;
                var domain = HistoryDomain();
                if (latestWindow || historyWindow == null) historyWindow = new TimeWindow(domain.To - historyWidth, domain.To);
                historyWindow = HistorySelection.Clamp(domain, historyWindow);
                historyWidth = historyWindow.To - historyWindow.From;
                historyNavigator.SetData(domain, historyOverview, historyWindow, historyData != null);
                ScheduleHistory();
                await LoadHistory();
            } catch (Exception ex) { if (revision == historyBoundsRevision) historyStats.Text = ex.Message; }
        }
        private void UpdateHistoryWidth() { historyWidth = historyScaleIndex == 5 ? HistoryDomain().To - HistoryDomain().From : new long[] { 600000, 1800000, 3600000, 21600000, 86400000 }[historyScaleIndex]; }
        private TimeWindow HistoryDomain()
        {
            long end = historyData?.To ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long start = Math.Min(historyData?.From ?? end - 600000, end - 600000);
            return new TimeWindow(Math.Min(start, calendarDay?.From ?? start), Math.Max(end, calendarDay?.To ?? end));
        }
        private TimeWindow CurrentHistory()
        {
            var domain = HistoryDomain();
            return HistorySelection.Clamp(domain, historyWindow ?? new TimeWindow(domain.To - historyWidth, domain.To));
        }
        private void MoveHistoryTo(long end)
        {
            if (historyNavigator == null) return;
            historyWindow = HistorySelection.Clamp(HistoryDomain(), new TimeWindow(end - historyWidth, end));
            historyWidth = historyWindow.To - historyWindow.From;
            ScheduleHistory();
        }
        private void ScheduleHistory()
        {
            historyChart.ClearSelection();
            historyRevision++; historyWindow = CurrentHistory();
            var start = DateTimeOffset.FromUnixTimeMilliseconds(historyWindow.From).LocalDateTime;
            var end = DateTimeOffset.FromUnixTimeMilliseconds(historyWindow.To).LocalDateTime;
            historyWidth = historyWindow.To - historyWindow.From;
            historyNavigator.SetSelection(historyWindow);
            historySpanPicker?.Update(historyWidth, HistoryDomain().To - HistoryDomain().From, historyData != null);
            var widths = new long[] { 600000, 1800000, 3600000, 21600000, 86400000 };
            historyScaleIndex = Array.IndexOf(widths, historyWidth);
            if (historyScaleIndex < 0 && historyWidth == HistoryDomain().To - HistoryDomain().From) historyScaleIndex = 5;
            settingHistory = true;
            for (int i = 0; i < historyPresets.Count; i++) historyPresets[i].IsChecked = i == historyScaleIndex;
            settingHistory = false;
            historyRange.Text = start.ToString("HH:mm:ss") + "  —  " + end.ToString("HH:mm:ss");
            historyBounds.Text = start.Date == end.Date ? start.ToString("yyyy/MM/dd (ddd)") : start.ToString("yyyy/MM/dd") + "  →  " + end.ToString("yyyy/MM/dd");
            historyRange.ToolTip = LocalTime(historyWindow.From) + "  →  " + LocalTime(historyWindow.To);
            long travel = historyData == null ? 0 : Math.Max(0, historyData.To - historyData.From - historyWidth);
            long earliest = HistoryDomain().From;
            historyBack.IsEnabled = historyData != null && historyWindow.From > earliest;
            historyForward.IsEnabled = historyData != null && historyWindow.To < HistoryDomain().To;
            historyFirst.Text = historyData == null ? T("記録はまだありません", "No recordings yet") : T("最初  ", "First  ") + TimelineTime(historyData.From);
            historyLast.Text = historyData == null ? "" : T("最新  ", "Latest  ") + TimelineTime(historyData.To - 1);
            historyPosition.Text = historyData == null ? T("記録待ち", "Awaiting recordings") : travel == 0 ? T("全記録を表示", "All recordings shown") : historyWindow.To >= historyData.To ? T("最新の期間", "Latest window") : T("過去の期間", "Earlier window");
            historyLoaded = false; exportButton.IsEnabled = false;
            historyStats.Text = historyNavigator.IsInteracting ? T("範囲を調整中 · グラフは概要表示", "Adjusting range · chart preview") : T("読み込み中…", "Loading…");
            historyChart.Average = null;
            historyChart.Points = HistorySelection.Preview(historyPreview, historyWindow); historyChart.From = historyWindow.From; historyChart.To = historyWindow.To;
            historyChart.GapMs = Math.Max(15000, (historyWindow.To - historyWindow.From) / 400 * 2); historyChart.InvalidateVisual();
            historyDebounce.Stop(); if (!historyNavigator.IsInteracting) historyDebounce.Start();
        }
        private static string LocalTime(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        private static string TimelineTime(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("MM/dd HH:mm");
        private async Task LoadHistory()
        {
            historyDebounce.Stop(); if (historyWindow == null) return;
            var range = historyWindow; int revision = historyRevision;
            try {
                var result = await Task.Run(() => { using (var store = new Store(database)) return Tuple.Create(store.Summary(range.From, range.To), store.Chart(range.From, range.To)); });
                if (revision != historyRevision || closing) return;
                var summary = result.Item1;
                historyChart.Average = summary.Count > 0 ? (double?)summary.Average : null;
                historyStats.Text = summary.Count == 0 ? T("この期間に有効な心拍データはありません。", "No valid heart-rate samples in this window.") : T($"{summary.Count:N0} 件 · 最小 {summary.Min}   平均 {summary.Average:0.0}   最大 {summary.Max} bpm", $"{summary.Count:N0} samples · Min {summary.Min}   Avg {summary.Average:0.0}   Max {summary.Max} bpm");
                historyChart.Points = result.Item2; historyChart.From = range.From; historyChart.To = range.To; historyChart.GapMs = Math.Max(15000, (range.To - range.From) / 400 * 2); historyChart.InvalidateVisual();
                historyLoaded = true; exportButton.IsEnabled = historyData != null && !exportingHistory;
            } catch (Exception ex) { if (revision == historyRevision) historyStats.Text = T("読込エラー: ", "Read failed: ") + ex.Message; }
        }
        private async void ExportClicked(object sender, RoutedEventArgs e)
        {
            if (historyWindow == null || !historyLoaded || exportingHistory) return;
            var range = historyWindow;
            try {
                var dialog = new SaveFileDialog { FileName = "heartrate-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv", Filter = "CSV (*.csv)|*.csv", AddExtension = true, DefaultExt = ".csv" };
                if (dialog.ShowDialog(this) != true) return;
                exportingHistory = true; exportButton.IsEnabled = false; exportStatus.Text = T("CSVを書き出し中…", "Exporting CSV…");
                var count = await Task.Run(() => { using (var store = new Store(database)) return store.Export(dialog.FileName, range.From, range.To); });
                exportStatus.Text = T($"{count:N0}件を書き出しました: ", $"Exported {count:N0} samples: ") + dialog.FileName;
            } catch (Exception ex) { exportStatus.Text = T("書き出しエラー: ", "Export failed: ") + ex.Message; }
            finally { exportingHistory = false; exportButton.IsEnabled = historyLoaded && historyData != null; }
        }
    }
}
