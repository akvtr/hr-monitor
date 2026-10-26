using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private TextBlock connection, deviceTitle, advice, bpmText, lastText, oscText, oscDetail, errorText, liveStats, settingsStatus, testStatus;
        private HeartChart liveChart, historyChart;
        private ComboBox devices, themeChoice, languageChoice, retentionChoice, oscTimeoutMode;
        private TextBox host, port, intName, floatName, percentMin, percentMax, testBpm, oscTimeoutSeconds;
        private CheckBox oscEnabled, autoConnect, autoReconnect, closeToTray;
        private Button scanButton, connectButton, autoButton, stopButton, retryButton, testButton;
        private Popup connectionPopup;
        private Ellipse connectionDot;
        private TabControl tabs;
        private readonly System.Collections.Generic.List<RadioButton> livePresets = new System.Collections.Generic.List<RadioButton>();
        private static readonly long[] livePresetWidths = { 600000, 1800000, 3600000 };
        private long liveWidth = 600000;
        private TimeSpanPicker liveSpanPicker;
        private static TextBlock Label(string text, double size = 14, string color = "TextMuted")
        {
            var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, color); return label;
        }
        private static TextBlock Heading(string text)
        {
            var label = Label(text, 18, "Text"); label.FontWeight = FontWeights.SemiBold;
            label.Margin = new Thickness(0, 0, 0, 10); return label;
        }
        private static TextBlock Eyebrow(string text) { var label = Label(text, 11, "Accent"); label.FontWeight = FontWeights.SemiBold; return label; }
        private static TextBox Input(string text = "") => new TextBox { Text = text, Margin = new Thickness(0, 5, 0, 14), MinWidth = 90 };
        private static Button Button(string text, RoutedEventHandler handler, bool primary = false, string id = null, bool compact = false, double? width = null)
        {
            double standardWidth = compact ? 48 : width ?? 136;
            var button = new Button { Content = text, Width = standardWidth, MinWidth = standardWidth, Height = 44, Padding = new Thickness(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            if (primary) { button.SetResourceReference(Control.BackgroundProperty, "Accent"); button.SetResourceReference(Control.ForegroundProperty, "AccentInk"); button.SetResourceReference(Control.BorderBrushProperty, "Accent"); }
            if (id != null) AutomationProperties.SetAutomationId(button, id);
            AutomationProperties.SetName(button, text);
            button.Click += handler; return button;
        }
        private static Border Card(UIElement child, Thickness? padding = null, string fill = "Surface")
        {
            var card = new Border { CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(22), Child = child, BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, fill); card.SetResourceReference(Border.BorderBrushProperty, "Line"); return card;
        }
        private static void AddActionIcon(Button button, string geometry)
        {
            string text = (string)button.Content;
            var icon = new Path { Data = Geometry.Parse(geometry), Width = 14, Height = 14, Stretch = Stretch.Uniform,
                StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
            icon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            button.Content = icon;
            button.Width = button.MinWidth = 44;
            button.Height = 44;
            if (button.ToolTip == null) button.ToolTip = text;
            ToolTipService.SetShowOnDisabled(button, true);
        }
        private static StackPanel Stack(params UIElement[] children) { var stack = new StackPanel(); foreach (var child in children) stack.Children.Add(child); return stack; }
        private static StackPanel Horizontal(params UIElement[] children) { var stack = Stack(children); stack.Orientation = Orientation.Horizontal; return stack; }
        private static CheckBox Check(string text, bool value) => new CheckBox { Content = text, IsChecked = value, MinHeight = 42, Margin = new Thickness(0, 2, 0, 5), VerticalContentAlignment = VerticalAlignment.Center };
        private static ComboBox Choice(string[] options, int selected, double width = 150)
        {
            var combo = new ComboBox { Width = width, Height = 44, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var option in options) combo.Items.Add(option); combo.SelectedIndex = selected; return combo;
        }
        private static UIElement Field(string name, Control control, string helper = null)
        {
            AutomationProperties.SetName(control, name);
            var stack = new StackPanel();
            var label = Label(name, 12, "TextMuted"); label.Margin = new Thickness(0, 0, 0, 2);
            AutomationProperties.SetLabeledBy(control, label);
            stack.Children.Add(label); stack.Children.Add(control);
            if (!string.IsNullOrWhiteSpace(helper)) { var note = Label(helper, 11); note.Margin = new Thickness(0, -8, 0, 12); stack.Children.Add(note); }
            return stack;
        }
        private static UIElement ScrollPage(Grid grid, double minimumHeight)
        {
            grid.MinHeight = minimumHeight;
            var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.SizeChanged += (s, e) => grid.Height = Math.Max(minimumHeight, scroll.ActualHeight);
            return scroll;
        }
        private void BuildUi()
        {
            if (connectionPopup != null) connectionPopup.IsOpen = false;
            int selectedPage = Math.Max(0, tabs?.SelectedIndex ?? 0);
            historyRevision++; historyBoundsRevision++; historyDebounce.Stop();
            connection = Label(""); deviceTitle = Label(string.IsNullOrWhiteSpace(settings.LastName) ? T("心拍センサー", "Heart-rate sensor") : settings.LastName, 16, "Text");
            advice = Label("", 12); errorText = Label("", 12, "Danger");
            bpmText = Label("—", 56, "Text"); lastText = Label("", 12);
            oscText = Label("", 14, "Text"); oscText.FontWeight = FontWeights.SemiBold;
            oscDetail = Label("", 12); oscDetail.FontFamily = new FontFamily("Cascadia Mono, Consolas, Yu Gothic UI");
            testStatus = Label("", 12); settingsStatus = Label("", 13, "Accent");
            liveStats = Label("", 12); liveChart = new HeartChart { MinHeight = 210 }; historyChart = new HeartChart { MinHeight = 260 };
            ConfigureChartInteraction(liveChart, false); ConfigureChartInteraction(historyChart, true);
            devices = new ComboBox { MinWidth = 250, MaxWidth = 420, Height = 44, Margin = new Thickness(0, 0, 10, 0) };
            AutomationProperties.SetName(devices, T("接続する心拍センサー", "Heart-rate sensor to connect"));
            devices.SelectionChanged += (s, e) => { if (connectButton != null) connectButton.IsEnabled = !IsConnecting && !scanning && devices.SelectedItem != null; };

            var root = new Grid { Margin = new Thickness(24, 12, 24, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            autoButton = Button(T("接続", "Connect"), (s, e) => StartAutomatic(), false, "AutoConnect", width: 108);
            stopButton = Button(T("停止", "Stop"), StopClicked, false, "StopConnection", width: 108);
            var choose = Button(T("詳細", "Details"), (s, e) => connectionPopup.IsOpen = true, id: "ChooseSensor", width: 108);
            choose.ToolTip = T("機器選択・再接続・ログを開く", "Show sensor selection, reconnect and connection log");
            retryButton = Button(T("再接続", "Reconnect"), RetryClicked, id: "Reconnect", width: 108);
            var log = Button(T("ログコピー", "Copy log"), (s, e) => { try { Clipboard.SetText(connectionLog.Snapshot()); errorText.Text = T("接続ログをコピーしました。", "Connection log copied."); } catch (Exception ex) { errorText.Text = ex.Message; } }, id: "CopyConnectionLog", width: 108);
            scanButton = Button(T("再検索", "Scan"), ScanClicked, width: 96);
            connectButton = Button(T("選択した機器に接続", "Connect selected"), ConnectClicked, width: 188);
            foreach (var action in new[] { autoButton, stopButton, choose, retryButton, log, scanButton, connectButton }) { action.Height = 36; action.Style = (Style)FindResource("ConnectionAction"); }
            AddActionIcon(autoButton, "M4,1 L12,7 4,13 Z");
            AddActionIcon(stopButton, "M2,2 L12,2 12,12 2,12 Z");
            AddActionIcon(choose, "M1,3 L13,3 M1,7 L13,7 M1,11 L13,11");
            devices.Height = 36;

            connectionDot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var status = Horizontal(connectionDot, connection); status.HorizontalAlignment = HorizontalAlignment.Left;
            deviceTitle.FontSize = 14; deviceTitle.FontWeight = FontWeights.SemiBold; deviceTitle.MaxWidth = 320;
            deviceTitle.TextWrapping = TextWrapping.NoWrap; deviceTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            deviceTitle.Margin = new Thickness(0, 0, 16, 0);
            var deviceInfo = Horizontal(deviceTitle, status); deviceInfo.VerticalAlignment = VerticalAlignment.Center;
            // The details popup is outside layout and cannot resize the chart area.
            var connectionGrid = new Grid();
            Grid.SetIsSharedSizeScope(connectionGrid, true);
            connectionGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) connectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ConnectionAction" + i });
            for (int i = 0; i < 2; i++) connectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            connectionGrid.Children.Add(deviceInfo);
            var primaryActions = new[] { autoButton, stopButton, choose };
            foreach (var action in primaryActions) action.Style = (Style)FindResource("ConnectionIconAction");
            for (int i = 0; i < primaryActions.Length; i++) { var action = primaryActions[i]; action.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0); Grid.SetColumn(action, i + 1); connectionGrid.Children.Add(action); }
            advice.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(advice, 1); Grid.SetColumnSpan(advice, 4); connectionGrid.Children.Add(advice);
            var closeDetails = Button(T("閉じる", "Close"), (s, e) => { connectionPopup.IsOpen = false; choose.Focus(); }, id: "CloseConnectionDetails", width: 88);
            closeDetails.Height = 36; closeDetails.Margin = new Thickness(16, 0, 0, 0);
            closeDetails.Style = (Style)FindResource("ConnectionAction");
            var detailHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(closeDetails, Dock.Right); detailHeader.Children.Add(closeDetails);
            detailHeader.Children.Add(Label(T("接続の詳細", "Connection details"), 16, "Text"));
            devices.MaxWidth = double.PositiveInfinity; devices.HorizontalAlignment = HorizontalAlignment.Stretch;
            devices.Margin = new Thickness(0, 5, 0, 12);
            var manualControls = Horizontal(scanButton, connectButton);
            manualControls.Margin = new Thickness(0, 0, 0, 12);
            var secondaryActions = new Grid();
            Grid.SetIsSharedSizeScope(secondaryActions, true);
            for (int i = 0; i < 2; i++) secondaryActions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ConnectionAction" + i });
            secondaryActions.Children.Add(retryButton); Grid.SetColumn(log, 1); secondaryActions.Children.Add(log);
            var detailCard = Card(Stack(detailHeader, Field(T("心拍センサー", "Heart-rate sensor"), devices), manualControls, secondaryActions), new Thickness(20));
            detailCard.Width = 480;
            KeyboardNavigation.SetTabNavigation(detailCard, KeyboardNavigationMode.Cycle);
            connectionPopup = new Popup { Child = detailCard, PlacementTarget = choose, Placement = PlacementMode.Bottom,
                HorizontalOffset = choose.Width - detailCard.Width, VerticalOffset = 8, StaysOpen = false, AllowsTransparency = true };
            AutomationProperties.SetAutomationId(detailCard, "ConnectionDetails");
            AutomationProperties.SetName(detailCard, T("接続の詳細", "Connection details"));
            connectionPopup.Opened += (s, e) => devices.Focus();
            detailCard.PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Escape) { connectionPopup.IsOpen = false; choose.Focus(); e.Handled = true; }
            };
            choose.IsVisibleChanged += (s, e) => { if (!choose.IsVisible) connectionPopup.IsOpen = false; };
            connectionGrid.Children.Add(connectionPopup);
            var connectionCard = new Border { Child = connectionGrid, Padding = new Thickness(0, 8, 0, 12), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 6) };
            connectionCard.SetResourceReference(Border.BorderBrushProperty, "Line");
            Grid.SetRow(connectionCard, 0); root.Children.Add(connectionCard);

            tabs = new TabControl { TabStripPlacement = Dock.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            AutomationProperties.SetAutomationId(tabs, "MainTabs");
            var views = new[] { LiveView(), HistoryView(), SettingsView() };
            var names = new[] { T("ライブ", "Live"), T("履歴", "History"), T("設定", "Settings") };
            var descriptions = new[] { T("ライブ心拍", "Live heart rate"), T("履歴・CSV出力", "History & CSV export"), T("設定", "Settings") };
            var icons = new[] {
                "M 1,8 L 4,8 6,3 9,13 11,8 15,8",
                "M 2,2 L 2,14 15,14 M 5,10 L 8,6 11,8 15,3",
                "M 2,4 L 14,4 M 2,12 L 14,12 M 6,1 L 6,7 M 10,9 L 10,15"
            };
            for (int i = 0; i < views.Length; i++) {
                var icon = new Path { Data = Geometry.Parse(icons[i]), Width = 16, Height = 16, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Focusable = false };
                icon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(TabItem), 1) });
                var label = new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center };
                var tab = new TabItem { Header = Horizontal(icon, label), Content = views[i], ToolTip = descriptions[i] };
                AutomationProperties.SetAutomationId(tab, "Tab" + i); AutomationProperties.SetName(tab, descriptions[i]); tabs.Items.Add(tab);
            }
            tabs.SelectionChanged += async (s, e) => {
                if (e.Source != tabs) return;
                if (tabs.SelectedIndex == 1) await RefreshHistory(false);
            };
            Grid.SetRow(tabs, 1); root.Children.Add(tabs); errorText.MaxHeight = 45; errorText.Margin = new Thickness(2, 8, 0, 0); Grid.SetRow(errorText, 2); root.Children.Add(errorText);
            var shell = new DockPanel();
            var toolbar = CreateWindowToolbar(); DockPanel.SetDock(toolbar, Dock.Top);
            shell.Children.Add(toolbar); shell.Children.Add(root);
            Content = shell; tabs.SelectedIndex = Math.Min(selectedPage, tabs.Items.Count - 1);
            RenderConnection(); Tick();
        }
        private UIElement LiveView()
        {
            livePresets.Clear();
            bpmText.FontFamily = new FontFamily("Bahnschrift, Segoe UI"); bpmText.FontWeight = FontWeights.Light;
            AutomationProperties.SetAutomationId(bpmText, "LiveHeartRate");
            var bpmLine = Horizontal(bpmText, Label("bpm", 16, "TextMuted")); bpmLine.VerticalAlignment = VerticalAlignment.Center;
            bpmText.Margin = new Thickness(0, 0, 8, 0);
            var summary = Stack(lastText, liveStats); summary.Margin = new Thickness(24, 0, 0, 0); summary.VerticalAlignment = VerticalAlignment.Center;
            liveStats.Margin = new Thickness(0, 6, 0, 0);
            var title = Heading(T("心拍の推移", "Heart rate trend")); title.Margin = new Thickness(0, 0, 0, 4);
            var current = Stack(title, Horizontal(bpmLine, summary));

            var presets = new StackPanel { Orientation = Orientation.Horizontal };
            var labels = new[] { T("10分", "10 min"), T("30分", "30 min"), T("1時間", "1 hour") };
            for (int i = 0; i < livePresetWidths.Length; i++) {
                long width = livePresetWidths[i];
                var option = new RadioButton { Content = labels[i], GroupName = "LiveTimeSpan", IsChecked = liveWidth == width, Style = (Style)FindResource("TimeRangeOption") };
                AutomationProperties.SetAutomationId(option, "LiveScale" + i);
                AutomationProperties.SetName(option, T("表示幅 ", "Time span ") + labels[i]);
                option.Checked += (s, e) => { liveWidth = width; liveChart.ClearSelection(); Tick(); };
                presets.Children.Add(option); livePresets.Add(option);
            }
            liveSpanPicker = new TimeSpanPicker("LiveSpanSlider") { Margin = new Thickness(0, 6, 4, 0) };
            liveSpanPicker.WidthChanged += width => { liveWidth = width; liveChart.ClearSelection(); Tick(); };
            var scale = Stack(presets, liveSpanPicker); scale.Margin = new Thickness(24, 0, 0, 0); scale.VerticalAlignment = VerticalAlignment.Center;
            var chartHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) }; chartHeader.ColumnDefinitions.Add(new ColumnDefinition()); chartHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chartHeader.Children.Add(current); Grid.SetColumn(scale, 1); chartHeader.Children.Add(scale);
            liveChart.GapMs = 15000;
            liveChart.MinHeight = 180; AutomationProperties.SetAutomationId(liveChart, "LiveChart");
            var chartContent = new Grid(); chartContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); chartContent.RowDefinitions.Add(new RowDefinition { MinHeight = 180 }); chartContent.Children.Add(chartHeader); Grid.SetRow(liveChart, 1); chartContent.Children.Add(liveChart);
            var grid = new Grid { MinHeight = 360 }; grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(Card(chartContent, new Thickness(20)));
            var retention = Label(RetentionNotice(settings.RetentionDays), 12); AutomationProperties.SetAutomationId(retention, "LiveRetentionNotice"); retention.Margin = new Thickness(2, 10, 2, 0); Grid.SetRow(retention, 1); grid.Children.Add(retention);
            var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.SizeChanged += (s, e) => grid.MinHeight = Math.Max(360, scroll.ActualHeight);
            return scroll;
        }
    }
}
