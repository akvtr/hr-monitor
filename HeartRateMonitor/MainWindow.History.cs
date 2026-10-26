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
        private TimeSpanPicker historySpanPicker;
        private readonly List<RadioButton> historyPresets = new List<RadioButton>();
        private static readonly long[] historyPresetWidths = { 600000, 1800000, 3600000, 21600000, 86400000 };
        private TextBlock historyStats, historyRange, historyBounds, exportStatus;
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
        private bool settingHistory, historyTimerAttached, historyLoaded, exportingHistory;
        private Task historyExport = Task.CompletedTask;
        private async Task RefreshActivity(bool force = false)
        {
            if ((activityLoading && !force) || activityCalendar == null) return;
            activityLoading = true; int revision = ++activityRevision; var month = activityCalendar.Month;
            try {
                var activity = await Task.Run(() => history.LoadActivity(month, TimeZoneInfo.Local));
                if (revision != activityRevision || closing) return;
                activityCalendar.SetDays(activity);
            } catch (Exception ex) { if (revision == activityRevision && !closing) activityCalendar.SetError(ex.Message); }
            finally { if (revision == activityRevision) { activityLoading = false; activityRefreshAt = DateTime.UtcNow.AddMinutes(1); } }
        }
        private async Task RefreshHistory(bool latestWindow)
        {
            if (latestWindow) calendarDay = null;
            _ = RefreshActivity();
            int revision = ++historyBoundsRevision;
            int selectionRevision = historyRevision;
            try {
                var bounds = await Task.Run(() => history.LoadBounds());
                if (revision != historyBoundsRevision || closing) return;
                historyData = bounds;
                var domain = HistoryDomain();
                if ((latestWindow && selectionRevision == historyRevision) || historyWindow == null) {
                    historyWindow = new TimeWindow(domain.To - historyWidth, domain.To);
                    activityCalendar.SetMonth(DateTimeOffset.FromUnixTimeMilliseconds(historyWindow.From).LocalDateTime);
                }
                historyWindow = HistorySelection.Clamp(domain, historyWindow);
                historyWidth = historyWindow.To - historyWindow.From;
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
            historyWindow = HistorySelection.Clamp(HistoryDomain(), new TimeWindow(end - historyWidth, end));
            historyWidth = historyWindow.To - historyWindow.From;
            ScheduleHistory();
        }
        private void ScheduleHistory()
        {
            historyChart.ClearSelection();
            historyChart.SleepIntervals.Clear();
            var domain = HistoryDomain();
            historyRevision++; historyWindow = CurrentHistory(domain);
            historyWidth = historyWindow.To - historyWindow.From;
            SynchronizeHistoryControls(domain);
            UpdateHistoryLabels(domain);
            historyLoaded = false; UpdateExportButtons();
            historyStats.Text = T("読み込み中…", "Loading…");
            historyChart.Average = null;
            historyChart.Points = new List<ChartPoint>(); historyChart.From = historyWindow.From; historyChart.To = historyWindow.To;
            historyChart.GapMs = Math.Max(15000, (historyWindow.To - historyWindow.From) / 400 * 2); historyChart.InvalidateVisual();
            historyDebounce.Stop(); historyDebounce.Start();
        }
        private void SynchronizeHistoryControls(TimeWindow domain)
        {
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
            activityCalendar.SetSelectedDate(start);
            if (historyDateInput != null && !historyDateInput.IsKeyboardFocused && !historyTimeInput.IsKeyboardFocused) {
                historyDateInput.Text = start.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture);
                historyTimeInput.Text = start.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (historyJumpStatus != null) historyJumpStatus.Text = "";
            historyRange.Text = start.ToString("HH:mm:ss") + "  —  " + end.ToString("HH:mm:ss");
            historyBounds.Text = start.Date == end.Date ? start.ToString("yyyy/MM/dd (ddd)") : start.ToString("yyyy/MM/dd") + "  →  " + end.ToString("yyyy/MM/dd");
            historyRange.ToolTip = LocalTime(historyWindow.From) + "  →  " + LocalTime(historyWindow.To);
            historyBack.IsEnabled = historyData != null && historyWindow.From > domain.From;
            historyForward.IsEnabled = historyData != null && historyWindow.To < domain.To;
        }
        private static string LocalTime(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
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
                historyChart.SleepIntervals = result.SleepIntervals;
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
