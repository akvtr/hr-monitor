using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private UIElement HistoryView()
        {
            activityRevision++; activityLoading = false; activityRefreshAt = DateTime.MinValue;
            activityCalendar = new ActivityCalendar();
            if (historyWindow != null) activityCalendar.SetMonth(DateTimeOffset.FromUnixTimeMilliseconds(historyWindow.From).LocalDateTime);
            activityCalendar.MonthChanged += date => { _ = RefreshActivity(true); };
            activityCalendar.DaySelected += date => {
                long from = DailyActivity.Midnight(date, TimeZoneInfo.Local), to = DailyActivity.Midnight(date.AddDays(1), TimeZoneInfo.Local);
                // Keep the requested day, including empty days, rather than clamping to a different date.
                calendarDay = new TimeWindow(from, to);
                historyWindow = calendarDay; historyWidth = to - from;
                ScheduleHistory();
            };
            historyStats = Label("", 12); historyRange = Label("", 26, "Text"); historyRange.FontWeight = FontWeights.SemiBold; historyRange.FontFamily = new FontFamily("Bahnschrift, Segoe UI");
            historyBounds = Label("", 12);
            exportStatus = Label("", 12);
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
            var plotGrid = new Grid(); plotGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); plotGrid.RowDefinitions.Add(new RowDefinition { MinHeight = 120 });
            plotGrid.Children.Add(Stack(toolbar, rangeRow, historyStats));
            historyChart.MinHeight = 120; AutomationProperties.SetAutomationId(historyChart, "HistoryChart"); Grid.SetRow(historyChart, 1); plotGrid.Children.Add(historyChart);

            historyChart.ToolTip = T("ホイールで前後に移動 · Ctrl＋ホイールで拡大・縮小 · ドラッグで区間を選択 · Escで解除", "Wheel to move through time · Ctrl+wheel to zoom · Drag to select an interval · Esc to clear");

            exportButton = Button(T("表示期間をCSV出力", "Export range to CSV"), ExportClicked, primary: true, id: "HistoryExport", width: 196); exportButton.Margin = new Thickness(16, 0, 0, 0); exportButton.IsEnabled = false;
            exportButton.ToolTip = T("表示期間に含まれる全測定値を書き出します。", "Exports every sample in the displayed range.");
            analysisExportButton = Button(T("分析区間をCSV出力", "Export interval to CSV"), AnalysisExportClicked, id: "HistoryAnalysisExport", width: 196);
            analysisExportButton.Margin = new Thickness(12, 0, 0, 0); analysisExportButton.IsEnabled = false;
            analysisExportButton.ToolTip = T("グラフでドラッグして区間を選択すると、その区間の全測定値を書き出せます。", "Drag on the chart to select an analysis interval, then export every sample in that interval.");
            foreach (var action in new[] { latest, historyBack, historyForward, exportButton, analysisExportButton }) {
                action.Style = (Style)FindResource("ConnectionAction");
                action.Height = 40;
            }
            var retention = Label(RetentionNotice(settings.RetentionDays), 12); AutomationProperties.SetAutomationId(retention, "HistoryRetentionNotice");
            sleepHistoryStatus = Label("", 11); sleepHistoryStatus.TextTrimming = TextTrimming.CharacterEllipsis; sleepHistoryStatus.TextWrapping = TextWrapping.NoWrap;
            AutomationProperties.SetAutomationId(sleepHistoryStatus, "HistorySleepStatus");
            UpdateSleepStatus();
            var csvActions = Horizontal(exportButton, analysisExportButton);
            var footerActions = new DockPanel(); DockPanel.SetDock(csvActions, Dock.Right); footerActions.Children.Add(csvActions); footerActions.Children.Add(Stack(retention, sleepHistoryStatus));
            exportStatus.Margin = new Thickness(0, 4, 0, 0);
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var navigation = new Grid(); navigation.ColumnDefinitions.Add(new ColumnDefinition()); navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); navigation.ColumnDefinitions.Add(new ColumnDefinition());
            navigation.Children.Add(activityCalendar);
            var dateControls = HistoryDateControls(); Grid.SetColumn(dateControls, 2);
            navigation.Children.Add(dateControls);
            var activityCard = Card(navigation, new Thickness(18)); activityCard.Margin = new Thickness(0, 0, 0, 10); grid.Children.Add(activityCard);
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
    }
}
