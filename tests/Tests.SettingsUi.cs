using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestSettingsRecoveryAfterConnection(string folder)
    {
        RunOnSta(() => {
            string language = Words.Language;
            MainWindow window = null;
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var settingsField = typeof(MainWindow).GetField("settings", members);
            var save = typeof(MainWindow).GetMethod("SaveClicked", members);
            Action<DeviceChoice> connect = choice => {
                var bluetooth = (BluetoothMonitor)typeof(MainWindow).GetField("bluetooth", members).GetValue(window);
                var connected = (Action<DeviceChoice>)typeof(BluetoothMonitor).GetField("Connected", members).GetValue(bluetooth);
                // Exercise the real connection subscriber without requiring a BLE sensor.
                connected(choice);
                WaitForDispatcherTask(window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
            };
            try {
                string directory = Path.Combine(folder, "settings-recovery-after-connection");
                string path = Path.Combine(directory, "settings.json");
                new Settings { Port = 9011, Language = "en", AutoConnect = false, OyasumiEnabled = false }.Save(path);
                File.Copy(path, path + ".bak");
                File.WriteAllText(path, "{\"Port\":0,\"Language\":\"en\",\"AutoConnect\":false,\"OyasumiEnabled\":false}");
                byte[] invalid = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
                window = new MainWindow(directory);
                Check(!((Settings)settingsField.GetValue(window)).OscEnabled, "settings recovery starts with OSC disabled after invalid settings");
                for (ulong address = 1; address <= 2; address++) {
                    connect(new DeviceChoice { Address = address, Name = "ISOLATED RECOVERY SENSOR", DeviceId = "recovery-device-" + address });
                    var active = (Settings)settingsField.GetValue(window);
                    Check(active.LastAddress == address && active.LastDeviceVerified && active.LastDeviceId == "recovery-device-" + address,
                        "connection updates device metadata in memory during settings recovery: " + address);
                    Check(File.ReadAllBytes(path).SequenceEqual(invalid) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup),
                        "repeated connection preserves invalid settings and valid recovery backup byte for byte: " + address);
                }
                string initialTheme = ((Settings)settingsField.GetValue(window)).Theme;
                typeof(MainWindow).GetMethod("ToggleThemeClicked", members).Invoke(window, new object[] { null, null });
                string selectedTheme = ((Settings)settingsField.GetValue(window)).Theme;
                Check(selectedTheme != initialTheme && (selectedTheme == "Light" || selectedTheme == "Dark"),
                    "toolbar theme remains usable in memory while settings recovery blocks automatic writes");
                Check(File.ReadAllBytes(path).SequenceEqual(invalid) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup),
                    "toolbar theme change preserves both invalid settings and the valid recovery backup");
                ((TextBox)typeof(MainWindow).GetField("port", members).GetValue(window)).Text = "9022";
                ((CheckBox)typeof(MainWindow).GetField("oyasumiEnabled", members).GetValue(window)).IsChecked = false;
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    save.Invoke(window, new object[] { null, null });
                    Check(((TextBlock)typeof(MainWindow).GetField("settingsStatus", members).GetValue(window)).Text.StartsWith("保存できません: "),
                        "explicit recovery save reports a locked settings destination");
                }
                connect(new DeviceChoice { Address = 3, Name = "ISOLATED RECOVERY SENSOR", DeviceId = "recovery-device-3" });
                Check(File.ReadAllBytes(path).SequenceEqual(invalid) && File.ReadAllBytes(path + ".bak").SequenceEqual(backup),
                    "failed explicit recovery save does not enable automatic writes or replace the good backup");
                save.Invoke(window, new object[] { null, null });
                var persisted = Settings.Load(path);
                Check(persisted.Port == 9022 && persisted.LastAddress == 3 && persisted.LastDeviceId == "recovery-device-3" && !persisted.OscEnabled && persisted.Theme == selectedTheme,
                    "successful explicit recovery save persists edited settings, selected theme and the latest verified device");
                Check(File.ReadAllBytes(path + ".bak").SequenceEqual(backup), "explicit recovery preserves the prior valid backup instead of backing up invalid settings");
                byte[] recovered = File.ReadAllBytes(path);
                connect(new DeviceChoice { Address = 4, Name = "ISOLATED RECOVERY SENSOR", DeviceId = "recovery-device-4" });
                Check(Settings.Load(path).LastAddress == 4 && Settings.Load(path).Port == 9022,
                    "automatic device persistence resumes after a successful explicit recovery save");
                Check(File.ReadAllBytes(path + ".bak").SequenceEqual(recovered), "normal backup rotation resumes using the recovered valid settings");
                window.EndSession(); window.Close(); window = null;

                foreach (bool existing in new[] { true, false }) {
                    directory = Path.Combine(folder, "settings-normal-connection-" + existing);
                    path = Path.Combine(directory, "settings.json");
                    if (existing) new Settings { Port = 9033, AutoConnect = false, OyasumiEnabled = false, OscEnabled = false }.Save(path);
                    window = new MainWindow(directory);
                    connect(new DeviceChoice { Address = 5, Name = "ISOLATED NORMAL SENSOR", DeviceId = "normal-device" });
                    persisted = Settings.Load(path);
                    Check(persisted.LastAddress == 5 && persisted.LastDeviceVerified && persisted.Port == (existing ? 9033 : 9000),
                        "connection automatically saves device metadata when settings load succeeds; existing=" + existing);
                    Check(File.Exists(path + ".bak") == existing, "normal connection creates a backup only when replacing an existing settings file; existing=" + existing);
                    window.EndSession(); window.Close(); window = null;
                }
            } finally {
                if (window != null) { window.EndSession(); window.Close(); }
                Words.Language = language;
            }
        }, "Settings recovery after connection verification failed");
    }

    private static void TestSettingsApplyAfterSave(string folder)
    {
        RunOnSta(() => {
            string language = Words.Language;
            MainWindow window = null;
            try {
                string directory = Path.Combine(folder, "settings-save-before-apply");
                string settingsPath = Path.Combine(directory, "settings.json");
                string database = Path.Combine(directory, "heartrate.db");
                new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false, Language = "ja" }.Save(settingsPath);
                long expired = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
                using (var store = new Store(database))
                    store.Add(new Reading { UnixMs = expired, Bpm = 70, Session = "ISOLATED SETTINGS SAVE TEST" });
                window = new MainWindow(directory);
                const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                var settingsField = typeof(MainWindow).GetField("settings", members);
                var original = (Settings)settingsField.GetValue(window);
                var engine = (RecordingEngine)typeof(MainWindow).GetField("engine", members).GetValue(window);
                var retention = (ComboBox)typeof(MainWindow).GetField("retentionChoice", members).GetValue(window);
                var languages = (ComboBox)typeof(MainWindow).GetField("languageChoice", members).GetValue(window);
                var host = (TextBox)typeof(MainWindow).GetField("host", members).GetValue(window);
                var save = typeof(MainWindow).GetMethod("SaveClicked", members);
                retention.SelectedIndex = 1; languages.SelectedIndex = 1; host.Text = "127.0.0.2";
                byte[] before = File.ReadAllBytes(settingsPath);
                using (var held = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    save.Invoke(window, new object[] { null, null });
                    var status = (TextBlock)typeof(MainWindow).GetField("settingsStatus", members).GetValue(window);
                    Check(status.Text.StartsWith("保存できません: "), "settings UI reports a locked settings file instead of claiming success");
                    Check(ReferenceEquals(settingsField.GetValue(window), original) && original.RetentionDays == 0 && original.Host == "127.0.0.1" && Words.Language == "ja",
                        "failed settings save preserves the active retention, endpoint and language");
                    Check(File.ReadAllBytes(settingsPath).SequenceEqual(before), "failed settings UI save preserves the persisted file byte for byte");
                    Check(retention.SelectedIndex == 1 && languages.SelectedIndex == 1 && host.Text == "127.0.0.2",
                        "failed settings save keeps unapplied form edits available for retry");
                }
                // A queued one-shot test completes after any queued retention change; it creates no readings.
                Throws<InvalidOperationException>(() => engine.TestAsync(80).GetAwaiter().GetResult(), "disabled OSC provides a writer barrier after failed settings save");
                using (var store = new Store(database))
                    Check(store.Query(expired, expired + 1).Count == 1, "failed settings save never applies destructive retention to existing recordings");
                save.Invoke(window, new object[] { null, null });
                var applied = (Settings)settingsField.GetValue(window);
                var persisted = Settings.Load(settingsPath);
                Check(applied.RetentionDays == 1 && applied.Host == "127.0.0.2" && Words.Language == "en" &&
                    persisted.RetentionDays == 1 && persisted.Host == applied.Host && persisted.Language == "en",
                    "retry after unlocking settings persists and applies all retained edits");
                Throws<InvalidOperationException>(() => engine.TestAsync(80).GetAwaiter().GetResult(), "disabled OSC provides a writer barrier after successful settings retry");
                using (var store = new Store(database))
                    Check(store.Query(expired, expired + 1).Count == 0, "retention deletes expired recordings only after settings save succeeds");
                Check(((TextBlock)typeof(MainWindow).GetField("settingsStatus", members).GetValue(window)).Text == "Settings saved and applied.",
                    "successful settings retry replaces the old error with localized success feedback");
            } finally {
                if (window != null) { window.EndSession(); window.Close(); }
                Words.Language = language;
            }
        }, "Settings save-before-apply verification failed");
    }
}
