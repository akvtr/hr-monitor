using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using HeartRateMonitor;

internal static partial class Tests
{
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
