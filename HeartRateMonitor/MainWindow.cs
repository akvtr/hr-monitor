using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow : Window
    {
        private readonly string folder, database, settingsPath;
        private readonly HistoryRepository history;
        private Settings settings;
        private readonly BluetoothMonitor bluetooth = new BluetoothMonitor();
        private readonly AutoConnector automatic;
        private readonly ConnectionLog connectionLog;
        private readonly RecordingEngine engine;
        private readonly CancellationTokenSource closingToken = new CancellationTokenSource();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly List<ChartPoint> livePoints = new List<ChartPoint>();
        private TrayHost tray;
        private ConnectionUpdate state = new ConnectionUpdate();
        private OscTransmission transmission;
        private string oscFailure;
        private Reading latest;
        private bool closing, canClose, exitRequested, smokeTest, scanning, stopping;
        private CancellationTokenSource scanCancellation;
        private Task manualSearch = Task.CompletedTask;
        private bool IsConnecting => automatic.Active || bluetooth.Active;
        private static string T(string ja, string en) => Words.T(ja, en);

        public MainWindow(string dataFolder)
        {
            folder = dataFolder; database = Path.Combine(folder, "heartrate.db"); settingsPath = Path.Combine(folder, "settings.json");
            history = new HistoryRepository(database);
            connectionLog = new ConnectionLog(folder); connectionLog.Add("Heart Rate Monitor " + AppVersion.Product + " / startup");
            string warning = null;
            try { settings = Settings.Load(settingsPath); }
            catch (Exception ex) { settings = new Settings { OscEnabled = false }; warning = "Settings could not be loaded. OSC disabled. " + ex.Message; }
            Words.Language = settings.Language;
            automatic = new AutoConnector(bluetooth);
            Title = "Heart Rate Monitor " + AppVersion.Product + " · BLE / VRChat";
            Icon = ProductIcon.CreateWindowIcon();
            Width = 1200; Height = 880; MinWidth = 1020; MinHeight = 760;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            InitializeWindowChrome();
            StateChanged += (s, e) => UpdateWindowControls();
            ThemeManager.Initialize(this); ThemeManager.Apply(this, settings.Theme);
            SetResourceReference(BackgroundProperty, "Background"); SetResourceReference(ForegroundProperty, "Text");
            FontFamily = new FontFamily("Segoe UI, Yu Gothic UI"); FontSize = 14;
            BuildUi();
            engine = new RecordingEngine(database, settings);
            bluetooth.Status += value => connectionLog.Add(value);
            bluetooth.Diagnostic += value => connectionLog.Add(value);
            automatic.Status += value => connectionLog.Add(value);
            bluetooth.StateChanged += value => Ui(() => SetState(value));
            automatic.StateChanged += value => Ui(() => SetState(value));
            automatic.Discovered += found => Ui(() => UpdateDevices(found));
            bluetooth.Connected += choice => Ui(() => {
                deviceTitle.Text = choice.Name;
                settings.LastAddress = choice.Address; settings.LastName = choice.Name;
                settings.LastDeviceVerified = true; settings.LastDeviceId = choice.DeviceId;
                settings.LastAddressType = choice.AddressType.HasValue ? (int)choice.AddressType.Value : -1;
                try { settings.Save(settingsPath); } catch (Exception ex) { errorText.Text = ex.Message; }
            });
            bluetooth.Received += engine.Accept;
            engine.Error += message => Ui(() => errorText.Text = T("記録エラー: ", "Recording error: ") + message);
            if (engine.StartupRetentionError != null) errorText.Text = T("古い記録の削除に失敗しました: ", "Could not delete expired recordings: ") + engine.StartupRetentionError;
            engine.RetentionChanged += () => Ui(() => { if (tabs.SelectedIndex == 1) _ = RefreshHistory(false); });
            engine.OscFailed += message => Ui(() => { oscFailure = message; UpdateOsc(); });
            engine.Transmitted += value => Ui(() => { transmission = value; oscFailure = null; UpdateOsc(); });
            engine.Processed += (row, saved, osc) => Ui(() => Received(row, saved, osc));
            timer.Tick += (s, e) => Tick(); timer.Start();
            Closing += OnClosing;
            SourceInitialized += (s, e) => ApplyTheme();
            SystemEvents.UserPreferenceChanged += SystemThemeChanged;
            Loaded += (s, e) => {
                tray = new TrayHost(() => Ui(RestoreWindow), () => Ui(StartAutomatic), () => Ui(() => StopClicked(null, null)), () => Ui(ExitApplication));
                if (!smokeTest && settings.AutoConnect) StartAutomatic();
            };
            if (warning != null) errorText.Text = warning;
            Tick();
        }
        private void Ui(Action action) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => { if (!canClose) action(); })); }
        public void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
        public void ExitApplication() { exitRequested = true; Close(); }
        public void EndSession()
        {
            closing = true; closingToken.Cancel(); scanCancellation?.Cancel(); timer.Stop(); historyDebounce.Stop();
            bluetooth.Received -= engine.Accept;
            // Discovery continuations may need the UI dispatcher. Do not synchronously
            // wait for them during Windows session shutdown; only drain the independent writer.
            _ = automatic.StopAsync(); _ = bluetooth.StopAsync();
            try { Task.Run(() => engine.StopAsync()).GetAwaiter().GetResult(); }
            catch (Exception ex) { connectionLog.Add("Session ending: " + ex.Message); }
            tray?.Dispose(); canClose = true;
        }
        private void ApplyTheme() { ApplyThemeTransition(false); }
        private void ApplyThemeTransition(bool animate) { ThemeManager.Apply(this, settings.Theme, animate, () => { liveChart.InvalidateVisual(); historyChart.InvalidateVisual(); }); UpdateThemeIcon(animate); liveChart.InvalidateVisual(); historyChart.InvalidateVisual(); }
        private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) { Ui(ApplyTheme); }
        private void SetState(ConnectionUpdate value)
        {
            if (stopping || closing) return;
            if (value.Problem == ConnectionProblem.None && (value.Phase == ConnectionPhase.Searching || value.Phase == ConnectionPhase.Detected || value.Phase == ConnectionPhase.Connecting || value.Phase == ConnectionPhase.Retrying))
                value = new ConnectionUpdate { Phase = value.Phase, Problem = state.Problem, Detail = state.Detail };
            state = value; RenderConnection();
        }
        private void RenderConnection()
        {
            connection.Text = ConnectionUpdate.Name(state.Phase);
            string statusColor = state.Phase == ConnectionPhase.Receiving ? "Success" :
                state.Phase == ConnectionPhase.NoSignal || state.Phase == ConnectionPhase.Retrying ? "Warning" :
                state.Phase == ConnectionPhase.Error ? "Danger" :
                state.Phase == ConnectionPhase.Stopped ? "TextMuted" : "Accent";
            connection.SetResourceReference(TextBlock.ForegroundProperty, statusColor);
            if (connectionDot != null) connectionDot.SetResourceReference(System.Windows.Shapes.Ellipse.FillProperty, statusColor);
            advice.SetResourceReference(TextBlock.ForegroundProperty, state.Phase == ConnectionPhase.Error ? "Danger" :
                state.Phase == ConnectionPhase.NoSignal || state.Problem != ConnectionProblem.None ? "Warning" : "TextMuted");
            advice.Text = state.Problem != ConnectionProblem.None ? ConnectionUpdate.Advice(state.Problem) :
                state.Phase == ConnectionPhase.AwaitingData ? T("通知を有効にしました。最初の心拍データを待っています。", "Notifications enabled. Waiting for the first heart-rate sample.") :
                state.Phase == ConnectionPhase.Receiving ? T("心拍データを受信しています。", "Heart-rate samples are arriving.") :
                state.Phase == ConnectionPhase.NoSignal ? T("装着状態とセンサーの電源を確認してください。", "Check skin contact and sensor power.") :
                state.Phase == ConnectionPhase.Stopped ? T("自動接続で再開できます。", "Choose Connect to resume.") : T("センサーを装着し、近くで電源を入れてください。", "Wear your sensor and turn it on nearby.");
            connection.ToolTip = state.Detail;
        }
        private void StartAutomatic()
        {
            if (closing || stopping || scanning || IsConnecting) return;
            latest = null; state = new ConnectionUpdate();
            SetState(new ConnectionUpdate { Phase = ConnectionPhase.Searching });
            automatic.Start(() => settings.Copy()); Tick();
        }
        private async void RetryClicked(object sender, RoutedEventArgs e) { await StopConnection(); StartAutomatic(); }
        private void UpdateDevices(List<DeviceChoice> found)
        {
            string selected = (devices.SelectedItem as DeviceChoice)?.DeviceId ?? settings.LastDeviceId;
            devices.Items.Clear(); foreach (var device in found) devices.Items.Add(device);
            devices.SelectedItem = found.FirstOrDefault(d => d.DeviceId == selected) ?? found.FirstOrDefault(); Tick();
        }
        private async void ScanClicked(object sender, RoutedEventArgs e) { manualSearch = SearchManually(); await manualSearch; }
        private async Task SearchManually()
        {
            scanCancellation?.Dispose(); scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(closingToken.Token);
            scanning = true; Tick(); SetState(new ConnectionUpdate { Phase = ConnectionPhase.Searching });
            try {
                var found = await bluetooth.ScanAsync(scanCancellation.Token); UpdateDevices(found);
                SetState(new ConnectionUpdate { Phase = found.Count == 0 ? ConnectionPhase.Error : ConnectionPhase.Detected, Problem = found.Count == 0 ? ConnectionProblem.NotFound : ConnectionProblem.None });
                if (found.Count == 0) advice.Text = T("電源と他のアプリの接続を確認し、再検索してください。", "Check sensor power and other app connections, then scan again.");
            } catch (OperationCanceledException) { }
            catch (Exception ex) { SetState(new ConnectionUpdate { Phase = ConnectionPhase.Error, Problem = ConnectionUpdate.Classify(ex), Detail = ex.Message }); }
            finally { scanning = false; Tick(); }
        }
        private void ConnectClicked(object sender, RoutedEventArgs e)
        {
            var choice = devices.SelectedItem as DeviceChoice; if (choice == null) return;
            latest = null; deviceTitle.Text = choice.Name; bluetooth.Start(choice, () => settings.AutoReconnect); Tick();
        }
        private async void StopClicked(object sender, RoutedEventArgs e) { await StopConnection(); }
        private async Task StopConnection()
        {
            if (stopping || closing) return;
            stopping = true; Tick();
            try { scanCancellation?.Cancel(); await automatic.StopAsync(); await bluetooth.StopAsync(); await manualSearch; latest = null; }
            catch (Exception ex) { errorText.Text = ex.Message; }
            finally { stopping = false; state = new ConnectionUpdate(); RenderConnection(); Tick(); }
        }
        private void Received(Reading row, string saved, string osc)
        {
            latest = row;
            bool valid = row.Bpm > 0 && row.Contact != false;
            if (valid) livePoints.Add(new ChartPoint { UnixMs = row.UnixMs, Bpm = row.Bpm, Min = row.Bpm, Max = row.Bpm });
            if (!stopping && IsConnecting) SetState(new ConnectionUpdate { Phase = valid ? ConnectionPhase.Receiving : ConnectionPhase.NoSignal });
            Tick();
        }
        private void Tick()
        {
            if (!closing && tabs.SelectedIndex == 1 && DateTime.UtcNow >= activityRefreshAt) _ = RefreshActivity();
            liveSpanPicker?.Update(liveWidth, 3600000);
            var presetWidths = new long[] { 600000, 1800000, 3600000 };
            for (int i = 0; i < livePresets.Count; i++) livePresets[i].IsChecked = liveWidth == presetWidths[i];
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            livePoints.RemoveAll(p => p.UnixMs < now - 3600000);
            var visible = livePoints.Where(p => p.UnixMs >= now - liveWidth).ToList();
            liveChart.Average = visible.Count > 0 ? (double?)visible.Average(p => p.Bpm) : null;
            liveChart.Points = visible; liveChart.From = now - liveWidth; liveChart.To = now; liveChart.InvalidateVisual();
            if (liveChart.SelectedRange != null && liveChart.SelectedRange.To <= liveChart.From) liveChart.ClearSelection();
            liveStats.Text = visible.Count > 0 ? T($"最小 {visible.Min(p => p.Bpm):0}   平均 {liveChart.Average:0.0}   最大 {visible.Max(p => p.Bpm):0} bpm", $"Min {visible.Min(p => p.Bpm):0}   Avg {liveChart.Average:0.0}   Max {visible.Max(p => p.Bpm):0} bpm") : T("最小 —   平均 —   最大 —", "Min —   Avg —   Max —");
            bool fresh = latest != null && now - latest.UnixMs <= 5000 && IsConnecting && !stopping;
            bpmText.Text = fresh && latest.Bpm > 0 && latest.Contact != false ? latest.Bpm.ToString() : "—";
            lastText.Text = latest == null ? T("受信待ち", "Awaiting samples") : fresh ? DateTimeOffset.FromUnixTimeMilliseconds(latest.UnixMs).LocalDateTime.ToString("HH:mm:ss") + " · " + (latest.Contact == false ? T("未装着", "No contact") : T("受信", "Received")) : T($"最終受信から {(now - latest.UnixMs) / 1000} 秒", $"Last sample {(now - latest.UnixMs) / 1000}s ago");
            if (!fresh && state.Phase == ConnectionPhase.Receiving) SetState(new ConnectionUpdate { Phase = ConnectionPhase.NoSignal, Problem = ConnectionProblem.NoData });
            scanButton.IsEnabled = !closing && !stopping && !scanning && !IsConnecting;
            devices.IsEnabled = scanButton.IsEnabled; connectButton.IsEnabled = scanButton.IsEnabled && devices.SelectedItem != null;
            autoButton.IsEnabled = scanButton.IsEnabled; retryButton.IsEnabled = !closing && !stopping;
            stopButton.IsEnabled = !closing && !stopping && (IsConnecting || scanning);
            UpdateOsc(); tray?.SetStatus(bpmText.Text == "—" ? ConnectionUpdate.Name(state.Phase) : bpmText.Text + " bpm");
        }
        private void UpdateOsc()
        {
            string status = !settings.OscEnabled ? T("無効", "Disabled") : oscFailure != null ? T("送信エラー: ", "Send failed: ") + oscFailure :
                transmission == null ? T("送信待ち", "Waiting to send") : (transmission.Timeout ? T("無受信のため0をUDP送信済み", "Timeout zeros sent via UDP") : transmission.Test ? T("テストUDP送信済み", "Test UDP sent") : T("UDP送信済み", "UDP sent")) + " · " + transmission.Time.ToString("HH:mm:ss");
            oscText.Text = status;
            oscText.SetResourceReference(TextBlock.ForegroundProperty, !settings.OscEnabled ? "TextMuted" : oscFailure != null ? "Danger" : transmission != null ? "Success" : "Accent");
            oscDetail.Text = settings.Host + ":" + settings.Port + "\nInt   " + Settings.Address(settings.IntParameter) + "\nFloat " + Settings.Address(settings.FloatParameter);
            if (transmission != null) oscDetail.Text += "\n" + T("最後の送信: ", "Last sent: ") + $"Int {transmission.Integer} / Float {transmission.Percent:0.000} → {transmission.Endpoint}";
            testButton.IsEnabled = settings.OscEnabled && !closing && !testingOsc;
        }
        private bool testingOsc;
        private async void TestOscClicked(object sender, RoutedEventArgs e)
        {
            int value;
            if (!int.TryParse(testBpm.Text, out value) || value < 1 || value > 65535) { testStatus.Text = T("1〜65535の整数を入力してください。", "Enter an integer from 1 to 65535."); return; }
            testingOsc = true; UpdateOsc();
            try { await engine.TestAsync(value); testStatus.Text = T("テスト送信しました。記録には保存しません。", "Test sent. Nothing was added to recordings."); }
            catch (Exception ex) { testStatus.Text = ex.Message; }
            finally { testingOsc = false; UpdateOsc(); }
        }
        private async void OnClosing(object sender, CancelEventArgs e)
        {
            if (canClose) return;
            e.Cancel = true;
            if (!exitRequested && !smokeTest && settings.CloseToTray && tray != null) { Hide(); tray.ExplainHidden(); return; }
            if (closing) return;
            closing = true; closingToken.Cancel(); scanCancellation?.Cancel(); timer.Stop(); historyDebounce.Stop(); IsEnabled = false;
            SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
            try { await automatic.StopAsync(); await bluetooth.StopAsync(); await manualSearch; await engine.StopAsync(); }
            catch (Exception ex) { connectionLog.Add("Shutdown: " + ex); }
            tray?.Dispose(); canClose = true; Close();
        }
    }
}
