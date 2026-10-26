using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace HeartRateMonitor
{
    public sealed partial class BluetoothMonitor
    {
        public async Task<List<DeviceChoice>> ScanAsync(CancellationToken token)
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(token);
            if (adapter == null || !adapter.IsLowEnergySupported) throw new InvalidOperationException("Bluetooth LE対応アダプターが見つかりません。");
            var found = new Dictionary<ulong, DeviceChoice>();
            Diagnostic?.Invoke("検索開始: BLE広告 + Windows登録済みデバイス/心拍サービス");
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
            async Task ListenAsync()
            {
                try { watcher.Start(); await Task.Delay(8000, token); }
                finally { watcher.Stop(); }
            }
            // Stop the watcher promptly on cancellation, but observe both searches before returning.
            var registeredTask = RegisteredDevicesAsync(token);
            await Task.WhenAll(ListenAsync(), registeredTask);
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
    }
}
