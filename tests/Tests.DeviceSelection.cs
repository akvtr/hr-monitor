using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestDeviceSelectionIdentity(string folder)
    {
        RunOnSta(() => {
            string directory = Path.Combine(folder, "device-selection");
            new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false, LastAddress = 2 }.Save(Path.Combine(directory, "settings.json"));
            var window = new MainWindow(directory);
            try {
                const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                var devices = (ComboBox)typeof(MainWindow).GetField("devices", members).GetValue(window);
                var updateMethod = typeof(MainWindow).GetMethod("UpdateDevices", members);
                Action<List<DeviceChoice>> update = found => updateMethod.Invoke(window, new object[] { found });
                var first = new DeviceChoice { Address = 1, Name = "Same name" };
                var selected = new DeviceChoice { Address = 2, Name = "Same name" };
                update(new List<DeviceChoice> { first, selected });
                devices.SelectedItem = selected;
                var refreshed = new DeviceChoice { Address = 2, Name = "Renamed sensor" };
                update(new List<DeviceChoice> { first, refreshed });
                Check(ReferenceEquals(devices.SelectedItem, refreshed), "device selection survives rediscovery when only a Bluetooth address is available");

                selected = new DeviceChoice { DeviceId = "selected-device", Name = "Device" };
                update(new List<DeviceChoice> { selected });
                refreshed = new DeviceChoice { Address = 20, DeviceId = "selected-device", Name = "Updated device" };
                update(new List<DeviceChoice> { first, refreshed });
                Check(ReferenceEquals(devices.SelectedItem, refreshed), "device selection retains a Windows device identity when its address becomes available");

                selected = new DeviceChoice { ServiceId = "selected-service", DeviceId = "", Name = "Service" };
                update(new List<DeviceChoice> { selected });
                refreshed = new DeviceChoice { ServiceId = "selected-service", DeviceId = "", Name = "Updated service" };
                update(new List<DeviceChoice> { new DeviceChoice { ServiceId = "other-service", DeviceId = "" }, refreshed });
                Check(ReferenceEquals(devices.SelectedItem, refreshed), "device selection distinguishes registered services with empty device IDs");

                first = new DeviceChoice { Address = 4, Name = "Other" };
                update(new List<DeviceChoice> { first, new DeviceChoice { Address = 5, Name = refreshed.Name } });
                Check(ReferenceEquals(devices.SelectedItem, first), "matching display names never identify a sensor after the selected device disappears");
                update(new List<DeviceChoice>());
                Check(devices.SelectedItem == null, "empty discovery clears the selected device");
                refreshed = new DeviceChoice { Address = 2, Name = "Saved sensor" };
                update(new List<DeviceChoice> { first, refreshed });
                Check(ReferenceEquals(devices.SelectedItem, refreshed), "device selection restores a saved Bluetooth address when no current selection exists");
            } finally { window.EndSession(); window.Close(); }
        }, "Device selection verification failed");
    }
}
