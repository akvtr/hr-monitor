using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

internal static partial class BluetoothDiagnostic
{
    [STAThread] public static int Main()
    {
        try { return Run().GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
    private static async Task<int> Run()
    {
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20))) {
            var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(timeout.Token);
            Console.WriteLine("BLE adapter available: " + (adapter?.IsLowEnergySupported == true));
            bool succeeded = await RunQueriesAsync(async () => {
                var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(timeout.Token);
                Console.WriteLine("Paired BLE endpoints: " + paired.Count);
                foreach (var device in paired) Console.WriteLine("Paired: " + device.Name + " | " + device.Id);
            }, async () => {
                var services = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.HeartRate)).AsTask(timeout.Token);
                Console.WriteLine("Registered heart-rate services: " + services.Count);
                foreach (var service in services) Console.WriteLine("Heart rate: " + service.Name + " | " + service.Id);
            }, Console.WriteLine);
            return succeeded ? 0 : 1;
        }
    }
}
