using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Devices.Enumeration;

namespace HeartRateMonitor
{
    public sealed class DeviceChoice
    {
        public ulong Address { get; set; }
        public string Name { get; set; }
        public bool HasHeartRateService { get; set; }
        public string DeviceId { get; set; }
        public string ServiceId { get; set; }
        public BluetoothAddressType? AddressType { get; set; }
        public override string ToString() => Address == 0 ? Name + Words.T(" · Windows登録済み", " · Registered in Windows") : $"{Name}  ·  {Address:X12}";
    }
    public sealed class BluetoothMonitor : IHeartRateDiscovery
    {
        public event Action<string> Status;
        public event Action<Reading> Received;
        public event Action<DeviceChoice> Connected;
        public event Action<string> Diagnostic;
        public event Action<ConnectionUpdate> StateChanged;
        private CancellationTokenSource lifetime;
        private Task running = Task.CompletedTask;
        private long lastReceived;
        private UnauthorizedAccessException accessFailure;
        public bool Active => !running.IsCompleted;

        public async Task<List<DeviceChoice>> ScanAsync(CancellationToken token)
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(token);
            if (adapter == null || !adapter.IsLowEnergySupported) throw new InvalidOperationException("Bluetooth LE対応アダプターが見つかりません。");
            var found = new Dictionary<ulong, DeviceChoice>();
            Diagnostic?.Invoke("検索開始: BLE広告 + Windows登録済みデバイス/心拍サービス");
            var registeredTask = RegisteredDevicesAsync(token);
            string stoppedError = null;
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += (s, e) => {
                string name = e.Advertisement.LocalName;
                bool heart = e.Advertisement.ServiceUuids.Contains(GattServiceUuids.HeartRate);
                if (!heart && string.IsNullOrWhiteSpace(name)) return;
                lock (found) {
                    DeviceChoice existing;
                    found.TryGetValue(e.BluetoothAddress, out existing);
                    found[e.BluetoothAddress] = new DeviceChoice {
                        Address = e.BluetoothAddress,
                        Name = string.IsNullOrWhiteSpace(name) ? (existing?.Name ?? "心拍センサー") : name,
                        HasHeartRateService = heart || existing?.HasHeartRateService == true,
                        AddressType = e.BluetoothAddressType
                    };
                }
            };
            watcher.Stopped += (s, e) => { if (e.Error != BluetoothError.Success) stoppedError = "Bluetooth検索: " + e.Error + "。WindowsのBluetooth設定を確認してください。"; };
            try {
                watcher.Start(); await Task.Delay(8000, token);
            } finally { watcher.Stop(); }
            var registered = await registeredTask;
            if (stoppedError != null) Diagnostic?.Invoke(stoppedError);
            List<DeviceChoice> results;
            lock (found) {
                foreach (var entry in registered.Where(d => d.Address != 0)) {
                    DeviceChoice advertisement;
                    if (found.TryGetValue(entry.Address, out advertisement)) {
                        entry.HasHeartRateService |= advertisement.HasHeartRateService;
                        entry.AddressType = advertisement.AddressType;
                        if (string.IsNullOrWhiteSpace(entry.Name)) entry.Name = advertisement.Name;
                    }
                    found[entry.Address] = entry;
                }
                results = found.Values.Concat(registered.Where(d => d.Address == 0)).OrderByDescending(d => d.HasHeartRateService).ThenBy(d => d.Name).ToList();
            }
            Diagnostic?.Invoke("検索結果: " + results.Count + "台 / " + string.Join("; ", results.Select(d => d.Name + " [心拍=" + d.HasHeartRateService + ", 登録=" + (!string.IsNullOrEmpty(d.DeviceId) || !string.IsNullOrEmpty(d.ServiceId)) + "]")));
            if (stoppedError != null && results.Count == 0) throw new InvalidOperationException(stoppedError);
            return results;
        }
        private async Task<List<DeviceChoice>> RegisteredDevicesAsync(CancellationToken token)
        {
            var results = new List<DeviceChoice>();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                try {
                    var services = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.HeartRate)).AsTask(timeout.Token);
                    foreach (var service in services) results.Add(new DeviceChoice { Name = string.IsNullOrWhiteSpace(service.Name) ? "Windowsの心拍センサー" : service.Name, ServiceId = service.Id, HasHeartRateService = true });
                    Diagnostic?.Invoke("Windows登録済み心拍サービス: " + services.Count);
                } catch (Exception ex) { if (token.IsCancellationRequested) return results; Diagnostic?.Invoke("心拍サービス列挙: " + ex.Message + $" (0x{ex.HResult:X8})"); }
                try {
                    var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), new[] { "System.Devices.Aep.DeviceAddress" }, DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token);
                    foreach (var device in devices) {
                        object raw; ulong address = 0;
                        if (device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out raw)) ulong.TryParse((raw?.ToString() ?? "").Replace(":", "").Replace("-", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
                        results.Add(new DeviceChoice { Name = device.Name, DeviceId = device.Id, Address = address });
                    }
                    Diagnostic?.Invoke("Windowsペアリング済みBLE機器: " + devices.Count);
                } catch (Exception ex) { if (token.IsCancellationRequested) return results; Diagnostic?.Invoke("ペアリング済み機器列挙: " + ex.Message + $" (0x{ex.HResult:X8})"); }
            }
            return results;
        }
        public void Start(DeviceChoice choice, Func<bool> reconnect)
        {
            if (Active) throw new InvalidOperationException("既に接続処理中です。");
            accessFailure = null;
            lifetime?.Dispose(); lifetime = new CancellationTokenSource();
            running = RunAsync(choice, reconnect, lifetime.Token);
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
                            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) throw new InvalidOperationException("心拍サービスに接続できません: " + services.Status + ", 件数=" + services.Services.Count + ", ATT=" + services.ProtocolError);
                            service = services.Services[0];
                            for (int i = 1; i < services.Services.Count; i++) services.Services[i].Dispose();
                            }
                            stage = "心拍サービスのアクセス許可・共有オープン";
                            await GattAccess.EnsureAsync(
                                () => service.RequestAccessAsync().AsTask(timeout.Token),
                                () => service.OpenAsync(GattSharingMode.SharedReadAndWrite).AsTask(timeout.Token),
                                message => Diagnostic?.Invoke(message), timeout.Token);
                            stage = "心拍通知の特性を取得";
                            var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.HeartRateMeasurement, BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                            if (characteristics.Status == GattCommunicationStatus.AccessDenied)
                                throw new UnauthorizedAccessException("アクセス許可後も心拍通知の取得が拒否されました: AccessDenied。接続ログのアクセス要求・共有オープンの結果を確認してください。");
                            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0) throw new InvalidOperationException("心拍通知を取得できません: " + characteristics.Status + ", ATT=" + characteristics.ProtocolError);
                            characteristic = characteristics.Characteristics[0];
                            handler = (s, e) => {
                                if (token.IsCancellationRequested) return;
                                try {
                                    var bytes = new byte[e.CharacteristicValue.Length];
                                    using (var reader = DataReader.FromBuffer(e.CharacteristicValue)) reader.ReadBytes(bytes);
                                    var measurement = Measurement.Parse(bytes);
                                    Interlocked.Exchange(ref lastReceived, DateTime.UtcNow.Ticks);
                                    Received?.Invoke(new Reading {
                                        UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Session = sessionId,
                                        Device = choice.ToString(), Bpm = measurement.Bpm, Contact = measurement.Contact,
                                        Rr = string.Join(";", measurement.RrMilliseconds.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))), Energy = measurement.Energy
                                    });
                                } catch (Exception ex) { Status?.Invoke("通知エラー · " + ex.Message); }
                            };
                            characteristic.ValueChanged += handler;
                            stage = "心拍通知を有効化";
                            var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(timeout.Token);
                            if (result != GattCommunicationStatus.Success) throw new InvalidOperationException("通知を有効にできません: " + result);
                        }
                        Interlocked.Exchange(ref lastReceived, DateTime.UtcNow.Ticks);
                        lastError = null;
                        Status?.Invoke("接続済み · " + choice.Name + " · 心拍通知を待っています");
                        Connected?.Invoke(choice);
                        StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.AwaitingData });
                        stage = "心拍通知を受信";
                        while (!token.IsCancellationRequested) {
                            await Task.Delay(1000, token);
                            if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) throw new InvalidOperationException("Bluetooth接続が切れました。");
                            if (new TimeSpan(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastReceived)).TotalSeconds > 20) throw new InvalidOperationException("20秒間通知がありません。装着と電源を確認してください。");
                        }
                    } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (UnauthorizedAccessException ex) { accessFailure = ex; lastError = stage + ": " + ex.Message; Status?.Invoke(lastError); Diagnostic?.Invoke(lastError); ReportFailure(ex); break; }
                    catch (OperationCanceledException ex) { lastError = stage + "でタイムアウトしました。"; Status?.Invoke(lastError); Diagnostic?.Invoke(lastError); ReportFailure(ex); }
                    catch (Exception ex) { lastError = stage + ": " + ex.Message; Status?.Invoke(lastError); Diagnostic?.Invoke(lastError + $" (0x{ex.HResult:X8})"); ReportFailure(ex); }
                    finally {
                        if (characteristic != null && handler != null) characteristic.ValueChanged -= handler;
                        Release(() => { if (session?.CanMaintainConnection == true) session.MaintainConnection = false; }, "接続保持の解除");
                        Release(() => session?.Dispose(), "セッションの解放");
                        Release(() => service?.Dispose(), "サービスの解放");
                        Release(() => device?.Dispose(), "機器の解放");
                    }
                    if (!reconnect() || token.IsCancellationRequested) break;
                    Status?.Invoke(lastError + " · 5秒後に再接続します");
                    StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Retrying, Problem = ConnectionUpdate.Classify(new Exception(lastError)), Detail = lastError });
                    await Task.Delay(5000, token);
                } while (!token.IsCancellationRequested);
            } catch (OperationCanceledException) { }
            finally { Status?.Invoke(token.IsCancellationRequested || lastError == null ? "停止中 · 接続ボタンで開始できます" : "停止中 · " + lastError); }
        }
        private void Release(Action cleanup, string stage)
        {
            try { cleanup(); } catch (Exception ex) { Diagnostic?.Invoke(stage + ": " + ex.Message); }
        }
        private void ReportFailure(Exception ex) => StateChanged?.Invoke(new ConnectionUpdate { Phase = ConnectionPhase.Error, Problem = ConnectionUpdate.Classify(ex), Detail = ex.Message });
    }
}
