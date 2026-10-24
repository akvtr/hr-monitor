using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

internal static class BluetoothDiagnostic
{
    [STAThread] public static int Main()
    {
        try { Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
    private static async Task Run()
    {
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20))) {
            var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(timeout.Token);
            Console.WriteLine("BLE adapter available: " + (adapter?.IsLowEnergySupported == true));
            try {
                var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(timeout.Token);
                Console.WriteLine("Paired BLE endpoints: " + paired.Count);
                foreach (var device in paired) Console.WriteLine("Paired: " + device.Name + " | " + device.Id);
            } catch (Exception ex) { Console.WriteLine("Paired query: " + ex.Message); }
            try {
                var services = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.HeartRate)).AsTask(timeout.Token);
                Console.WriteLine("Registered heart-rate services: " + services.Count);
                foreach (var service in services) Console.WriteLine("Heart rate: " + service.Name + " | " + service.Id);
            } catch (Exception ex) { Console.WriteLine("Service query: " + ex.Message); }
        }
    }
}
