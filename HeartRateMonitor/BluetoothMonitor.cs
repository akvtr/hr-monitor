using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HeartRateMonitor
{
    public sealed partial class BluetoothMonitor : IHeartRateDiscovery
    {
        public event Action<string> Status;
        public event Action<Reading> Received;
        public event Action<DeviceChoice> Connected;
        public event Action<string> Diagnostic;
        public event Action<ConnectionUpdate> StateChanged;
        private CancellationTokenSource lifetime;
        private Task running = Task.CompletedTask;
        private readonly ReceptionTimer receptionTimer = new ReceptionTimer();
        private UnauthorizedAccessException accessFailure;
        public bool Active => !running.IsCompleted;

        public void Start(DeviceChoice choice, Func<bool> reconnect)
        {
            if (choice == null) throw new ArgumentNullException(nameof(choice));
            if (reconnect == null) throw new ArgumentNullException(nameof(reconnect));
            if (Active) throw new InvalidOperationException("既に接続処理中です。");
            accessFailure = null;
            lifetime?.Dispose(); lifetime = new CancellationTokenSource();
            // Publish the operation before RunAsync can synchronously notify observers.
            var startup = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            running = startup.Task.Unwrap();
            startup.SetResult(RunAsync(choice, reconnect, lifetime.Token));
        }
        public async Task StopAsync()
        {
            lifetime?.Cancel();
            await running;
        }
        public async Task ConnectOnceAsync(DeviceChoice choice, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Start(choice, () => false);
            using (token.Register(() => lifetime?.Cancel())) await running;
            token.ThrowIfCancellationRequested();
            if (accessFailure != null) throw accessFailure;
        }
        private async Task RunAsync(DeviceChoice choice, Func<bool> reconnect, CancellationToken token)
        {
            string sessionId = Guid.NewGuid().ToString("N");
            string lastError = null;
            var lastProblem = ConnectionProblem.None;
            try {
                do {
                    BluetoothLEDevice device = null; GattDeviceService service = null; GattCharacteristic characteristic = null; GattSession session = null;
                    string stage = "機器を開く";
                    Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = null;
                    try {
                        Status?.Invoke("接続中 · " + choice.Name);
                        StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Connecting });
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                            timeout.CancelAfter(TimeSpan.FromSeconds(25));
                            if (!string.IsNullOrEmpty(choice.ServiceId)) {
                                stage = "登録済み心拍サービスを開く";
                                service = await GattDeviceService.FromIdAsync(choice.ServiceId, GattSharingMode.SharedReadAndWrite).AsTask(timeout.Token);
                                if (service == null) throw new InvalidOperationException("登録済み心拍サービスを開けません。");
                                device = await BluetoothLEDevice.FromIdAsync(service.Session.DeviceId.Id).AsTask(timeout.Token);
                            } else if (!string.IsNullOrEmpty(choice.DeviceId)) device = await BluetoothLEDevice.FromIdAsync(choice.DeviceId).AsTask(timeout.Token);
                            else if (choice.AddressType.HasValue) device = await BluetoothLEDevice.FromBluetoothAddressAsync(choice.Address, choice.AddressType.Value).AsTask(timeout.Token);
                            else device = await BluetoothLEDevice.FromBluetoothAddressAsync(choice.Address).AsTask(timeout.Token);
                            if (device == null) throw new InvalidOperationException("機器を開けません。電源とBluetoothの利用許可を確認してください。");
                            choice.Address = device.BluetoothAddress; choice.DeviceId = device.DeviceId;
                            if (!string.IsNullOrWhiteSpace(device.Name)) choice.Name = device.Name;
                            stage = "接続セッションを確立";
                            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(timeout.Token);
                            if (session?.CanMaintainConnection == true) session.MaintainConnection = true;
                            stage = "心拍サービスを取得";
                            if (service == null) {
                                var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.HeartRate, BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                                string detail = "心拍サービスに接続できません: " + services.Status + ", 件数=" + services.Services.Count + ", ATT=" + services.ProtocolError;
                                GattAccess.EnsureCommunication(services.Status, detail);
                                if (services.Services.Count == 0) throw new InvalidOperationException(detail);
                                service = services.Services[0];
                                for (int i = 1; i < services.Services.Count; i++) {
                                    var unused = services.Services[i];
                                    Release(() => unused.Dispose(), "未使用サービスの解放");
                                }
                            }
                            stage = "心拍サービスのアクセス許可・共有オープン";
                            await GattAccess.EnsureAsync(
                                () => service.RequestAccessAsync().AsTask(timeout.Token),
                                () => service.OpenAsync(GattSharingMode.SharedReadAndWrite).AsTask(timeout.Token),
                                message => Diagnostic?.Invoke(message), timeout.Token);
                            stage = "心拍通知の特性を取得";
                            var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                            string characteristicDetail = "心拍通知を取得できません: " + characteristics.Status + ", ATT=" + characteristics.ProtocolError;
                            GattAccess.EnsureCommunication(characteristics.Status, characteristicDetail);
                            if (characteristics.Characteristics.Count == 0) throw new InvalidOperationException(characteristicDetail);
                            characteristic = characteristics.Characteristics[0];
                            handler = (s, e) => HandleNotification(e, choice, sessionId, token);
                            characteristic.ValueChanged += handler;
                            stage = "心拍通知を有効化";
                            var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(timeout.Token);
                            GattAccess.EnsureCommunication(result, "通知を有効にできません: " + result);
                        }
                        receptionTimer.Reset();
                        lastError = null;
                        lastProblem = ConnectionProblem.None;
                        Status?.Invoke("接続済み · " + choice.Name + " · 心拍通知を待っています");
                        Connected?.Invoke(choice);
                        StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.AwaitingData });
                        stage = "心拍通知を受信";
                        while (!token.IsCancellationRequested) {
                            await Task.Delay(1000, token);
                            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) throw new InvalidOperationException("Bluetooth接続が切れました。");
                            if (receptionTimer.ElapsedSeconds > 20) throw new InvalidOperationException("20秒間通知がありません。装着と電源を確認してください。");
                        }
                    } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (UnauthorizedAccessException ex) { accessFailure = ex; lastError = stage + ": " + ex.Message; Status?.Invoke(lastError); Diagnostic?.Invoke(lastError); lastProblem = ReportFailure(ex); break; }
                    catch (OperationCanceledException ex) { lastError = stage + "でタイムアウトしました。"; Status?.Invoke(lastError); Diagnostic?.Invoke(lastError); lastProblem = ReportFailure(ex); }
                    catch (Exception ex) { lastError = stage + ": " + ex.Message; Status?.Invoke(lastError); Diagnostic?.Invoke(lastError + $" (0x{ex.HResult:X8})"); lastProblem = ReportFailure(ex); }
                    finally {
                        Release(() => { if (characteristic != null && handler != null) characteristic.ValueChanged -= handler; }, "通知ハンドラーの解除");
                        Release(() => { if (session?.CanMaintainConnection == true) session.MaintainConnection = false; }, "接続保持の解除");
                        Release(() => session?.Dispose(), "セッションの解放");
                        Release(() => service?.Dispose(), "サービスの解放");
                        Release(() => device?.Dispose(), "機器の解放");
                    }
                    if (!reconnect() || token.IsCancellationRequested) break;
                    Status?.Invoke(lastError + " · 5秒後に再接続します");
                    StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Retrying, Problem = lastProblem, Detail = lastError });
                    await Task.Delay(5000, token);
                } while (!token.IsCancellationRequested);
            } catch (OperationCanceledException) { }
            finally { Status?.Invoke(token.IsCancellationRequested || lastError == null ? "停止中 · 接続ボタンで開始できます" : "停止中 · " + lastError); }
        }
        private void HandleNotification(GattValueChangedEventArgs value, DeviceChoice choice, string sessionId, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            try {
                var bytes = new byte[value.CharacteristicValue.Length];
                using (var reader = DataReader.FromBuffer(value.CharacteristicValue)) reader.ReadBytes(bytes);
                var measurement = Measurement.Parse(bytes);
                receptionTimer.Reset();
                Received?.Invoke(new Reading {
                    UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Session = sessionId,
                    Device = choice.ToString(), Bpm = measurement.Bpm, Contact = measurement.Contact,
                    Rr = string.Join(";", measurement.RrMilliseconds.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))), Energy = measurement.Energy
                });
            } catch (Exception ex) { Status?.Invoke("通知エラー · " + ex.Message); }
        }
        private void Release(Action cleanup, string stage)
        {
            try { cleanup(); } catch (Exception ex) { Diagnostic?.Invoke(stage + ": " + ex.Message); }
        }
        private ConnectionProblem ReportFailure(Exception ex)
        {
            var problem = ConnectionUpdate.Classify(ex);
            StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Error, Problem = problem, Detail = ex.Message });
            return problem;
        }
    }
}
