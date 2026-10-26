using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private static readonly long[] historyPresetWidths = { 600000, 1800000, 3600000, 21600000, 86400000 };
        private List<ChartPoint> historyPreview = new List<ChartPoint>(), historyOverview = new List<ChartPoint>();
        private TextBlock historyStats, historyRange, historyBounds, historyFirst, historyLast, historyPosition, exportStatus;
        private Button exportButton, analysisExportButton, historyBack, historyForward;
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
        private Task historyExport = Task.CompletedTask;
        private async Task RefreshActivity()
        {
            if (activityLoading || activityCalendar == null) return;
            activityLoading = true; int revision = ++activityRevision; var today = DateTime.Today;
            try {
                var activity = await Task.Run(() => history.LoadActivity(today, TimeZoneInfo.Local));
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
            int selectionRevision = historyRevision;
            try {
                var snapshot = await Task.Run(() => history.LoadOverview());
                if (revision != historyBoundsRevision || closing) return;
                if (historyNavigator.IsInteracting) { historyRefreshPending = true; return; }
                historyData = snapshot.Bounds; historyPreview = snapshot.Preview; historyOverview = snapshot.Overview;
                var domain = HistoryDomain();
                if ((latestWindow && selectionRevision == historyRevision) || historyWindow == null)
                    historyWindow = new TimeWindow(domain.To - historyWidth, domain.To);
                historyWindow = HistorySelection.Clamp(domain, historyWindow);
                historyWidth = historyWindow.To - historyWindow.From;
                historyNavigator.SetData(domain, historyOverview, historyWindow, historyData != null);
                ScheduleHistory();
                await LoadHistory();
            } catch (Exception ex) { if (revision == historyBoundsRevision) historyStats.Text = ex.Message; }
        }
        private void UpdateHistoryWidth()
        {
            var domain = HistoryDomain();
            historyWidth = historyScaleIndex == historyPresetWidths.Length ? domain.To - domain.From : historyPresetWidths[historyScaleIndex];
        }
        private TimeWindow HistoryDomain()
        {
            long end = historyData?.To ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long start = Math.Min(historyData?.From ?? end - 600000, end - 600000);
            return new TimeWindow(Math.Min(start, calendarDay?.From ?? start), Math.Max(end, calendarDay?.To ?? end));
        }
        private TimeWindow CurrentHistory() => CurrentHistory(HistoryDomain());
        private TimeWindow CurrentHistory(TimeWindow domain)
        {
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
            var domain = HistoryDomain();
            historyRevision++; historyWindow = CurrentHistory(domain);
            historyWidth = historyWindow.To - historyWindow.From;
            SynchronizeHistoryControls(domain);
            UpdateHistoryLabels(domain);
            historyLoaded = false; UpdateExportButtons();
            historyStats.Text = historyNavigator.IsInteracting ? T("範囲を調整中 · グラフは概要表示", "Adjusting range · chart preview") : T("読み込み中…", "Loading…");
            historyChart.Average = null;
            historyChart.Points = HistorySelection.Preview(historyPreview, historyWindow); historyChart.From = historyWindow.From; historyChart.To = historyWindow.To;
            historyChart.GapMs = Math.Max(15000, (historyWindow.To - historyWindow.From) / 400 * 2); historyChart.InvalidateVisual();
            historyDebounce.Stop(); if (!historyNavigator.IsInteracting) historyDebounce.Start();
        }
        private void SynchronizeHistoryControls(TimeWindow domain)
        {
            historyNavigator.SetSelection(historyWindow);
            historySpanPicker?.Update(historyWidth, domain.To - domain.From, historyData != null);
            historyScaleIndex = Array.IndexOf(historyPresetWidths, historyWidth);
            if (historyScaleIndex < 0 && historyWidth == domain.To - domain.From) historyScaleIndex = historyPresetWidths.Length;
            settingHistory = true;
            for (int i = 0; i < historyPresets.Count; i++) historyPresets[i].IsChecked = i == historyScaleIndex;
            settingHistory = false;
        }
        private void UpdateHistoryLabels(TimeWindow domain)
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds(historyWindow.From).LocalDateTime;
            var end = DateTimeOffset.FromUnixTimeMilliseconds(historyWindow.To).LocalDateTime;
            historyRange.Text = start.ToString("HH:mm:ss") + "  —  " + end.ToString("HH:mm:ss");
            historyBounds.Text = start.Date == end.Date ? start.ToString("yyyy/MM/dd (ddd)") : start.ToString("yyyy/MM/dd") + "  →  " + end.ToString("yyyy/MM/dd");
            historyRange.ToolTip = LocalTime(historyWindow.From) + "  →  " + LocalTime(historyWindow.To);
            long travel = historyData == null ? 0 : Math.Max(0, historyData.To - historyData.From - historyWidth);
            historyBack.IsEnabled = historyData != null && historyWindow.From > domain.From;
            historyForward.IsEnabled = historyData != null && historyWindow.To < domain.To;
            historyFirst.Text = historyData == null ? T("記録はまだありません", "No recordings yet") : T("最初  ", "First  ") + TimelineTime(historyData.From);
            historyLast.Text = historyData == null ? "" : T("最新  ", "Latest  ") + TimelineTime(historyData.To - 1);
            historyPosition.Text = historyData == null ? T("記録待ち", "Awaiting recordings") : travel == 0 ? T("全記録を表示", "All recordings shown") : historyWindow.To >= historyData.To ? T("最新の期間", "Latest window") : T("過去の期間", "Earlier window");
        }
        private static string LocalTime(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        private static string TimelineTime(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("MM/dd HH:mm");
        private async Task LoadHistory()
        {
            historyDebounce.Stop(); if (historyWindow == null) return;
            var range = historyWindow; int revision = historyRevision;
            try {
                var result = await Task.Run(() => history.LoadWindow(range));
                if (revision != historyRevision || closing) return;
                var summary = result.Summary;
                historyChart.Average = summary.Count > 0 ? (double?)summary.Average : null;
                historyStats.Text = summary.Count == 0 ? T("この期間に有効な心拍データはありません。", "No valid heart-rate samples in this window.") : T($"{summary.Count:N0} 件 · 最小 {summary.Min}   平均 {summary.Average:0.0}   最大 {summary.Max} bpm", $"{summary.Count:N0} samples · Min {summary.Min}   Avg {summary.Average:0.0}   Max {summary.Max} bpm");
                historyChart.Points = result.Points; historyChart.From = range.From; historyChart.To = range.To; historyChart.GapMs = Math.Max(15000, (range.To - range.From) / 400 * 2); historyChart.InvalidateVisual();
                historyLoaded = true; UpdateExportButtons();
            } catch (Exception ex) { if (revision == historyRevision) historyStats.Text = T("読込エラー: ", "Read failed: ") + ex.Message; }
        }
        private void UpdateExportButtons()
        {
            bool ready = historyLoaded && historyData != null && !exportingHistory;
            if (exportButton != null) exportButton.IsEnabled = ready;
            if (analysisExportButton != null) analysisExportButton.IsEnabled = ready && historyChart.SelectedRange != null;
        }
        private async void ExportClicked(object sender, RoutedEventArgs e) => await ExportHistory(false);
        private async void AnalysisExportClicked(object sender, RoutedEventArgs e) => await ExportHistory(true);
        private async Task ExportHistory(bool analysis)
        {
            if (historyWindow == null || !historyLoaded || exportingHistory || historyData == null) return;
            var range = analysis ? historyChart.SelectedRange : historyWindow;
            if (range == null) return;
            try {
                var dialog = new SaveFileDialog {
                    Title = analysis ? T("分析区間をCSV出力", "Export analysis interval to CSV") : T("表示期間をCSV出力", "Export displayed range to CSV"),
                    FileName = (analysis ? "heartrate-interval-" : "heartrate-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv",
                    Filter = "CSV (*.csv)|*.csv", AddExtension = true, DefaultExt = ".csv"
                };
                if (dialog.ShowDialog(this) != true || closing) return;
                exportingHistory = true; UpdateExportButtons(); exportStatus.Text = T("CSVを書き出し中…", "Exporting CSV…");
                var export = Task.Run(() => history.Export(dialog.FileName, range));
                historyExport = export;
                var count = await export;
                exportStatus.Text = T($"{count:N0}件を書き出しました: ", $"Exported {count:N0} samples: ") + dialog.FileName;
            } catch (Exception ex) { exportStatus.Text = T("書き出しエラー: ", "Export failed: ") + ex.Message; }
            finally { historyExport = Task.CompletedTask; exportingHistory = false; UpdateExportButtons(); }
        }
    }
}
