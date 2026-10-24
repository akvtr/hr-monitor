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
            historyChart.ToolTip = T("ドラッグで区間を選択 · Ctrl＋ホイールで拡大・縮小 · Escで解除", "Drag to select an interval · Ctrl+wheel to zoom · Esc to clear");
            var timeline = Stack(timelineTitle, historyNavigator, endpoints); timeline.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(timeline, 2); plotGrid.Children.Add(timeline);

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
    }
}
