using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private UIElement OscTestControls()
        {
            testBpm = new TextBox { Text = "80", Width = 82, Height = 44, VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = T("テスト心拍数 / bpm", "Test heart rate / bpm") };
            AutomationProperties.SetName(testBpm, T("一回限りのOSCテスト心拍数", "One-shot OSC test heart rate"));
            testButton = Button(T("テスト送信", "Send test"), TestOscClicked, false, "TestOsc", width: 142);
            string testHelp = T("保存済みの設定で一度だけ送信します。測定記録には追加されません。", "Sends once using saved settings. No measurement is recorded.");
            testButton.ToolTip = testHelp;
            AutomationProperties.SetHelpText(testButton, testHelp);
            var testRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            testRow.Children.Add(Field(T("試験値（bpm）", "Test value (bpm)"), testBpm)); testRow.Children.Add(new Border { Width = 10 }); testRow.Children.Add(testButton);
            var oscFooter = Stack(testRow, testStatus, Label(T("UDP送信成功はVRChat側の受信確認ではありません。", "UDP success does not confirm receipt in VRChat."), 11));
            testStatus.Margin = new Thickness(0, 8, 0, 4);
            oscDetail.Margin = new Thickness(0, 9, 0, 14);
            return Stack(Heading(T("送信状況とテスト", "Transmission & test")), oscText, oscDetail, oscFooter);
        }
        private UIElement SettingsView()
        {
            host = Input(settings.Host); port = Input(settings.Port.ToString()); intName = Input(settings.IntParameter); floatName = Input(settings.FloatParameter); percentMin = Input(settings.PercentMin.ToString()); percentMax = Input(settings.PercentMax.ToString());
            oscEnabled = Check(T("OSC送信を有効にする", "Enable OSC transmission"), settings.OscEnabled);
            oscTimeoutMode = Choice(new[] { T("最終値を維持", "Keep last value"), T("無受信時に代替値を送信", "Send fallback values on timeout") }, settings.OscZeroOnTimeout ? 1 : 0, 360);
            oscTimeoutSeconds = Input(settings.OscTimeoutSeconds.ToString());
            oscTimeoutInt = Input(settings.OscTimeoutInt.ToString());
            oscTimeoutFloat = Input(settings.OscTimeoutFloat.ToString(CultureInfo.CurrentCulture));
            string timeoutHelp = T("起動または最終通知から計測し、停止中も対象です。代替値は換算せずOSCだけに一度送信し、記録しません。", "Measured since startup or the last notification, including while stopped. Fallbacks are sent once without conversion via OSC and are not recorded.");
            oscTimeoutSeconds.ToolTip = timeoutHelp;
            AutomationProperties.SetHelpText(oscTimeoutSeconds, timeoutHelp);
            Action updateTimeoutFields = () => {
                oscTimeoutSeconds.IsEnabled = oscTimeoutInt.IsEnabled = oscTimeoutFloat.IsEnabled = oscTimeoutMode.SelectedIndex == 1;
            };
            updateTimeoutFields();
            oscTimeoutMode.SelectionChanged += (s, e) => updateTimeoutFields();
            AutomationProperties.SetAutomationId(oscTimeoutMode, "OscTimeoutMode");
            AutomationProperties.SetAutomationId(oscTimeoutSeconds, "OscTimeoutSeconds");
            AutomationProperties.SetAutomationId(oscTimeoutInt, "OscTimeoutInt");
            AutomationProperties.SetAutomationId(oscTimeoutFloat, "OscTimeoutFloat");
            autoConnect = Check(T("起動時に自動接続する", "Connect automatically on startup"), settings.AutoConnect);
            autoReconnect = Check(T("切断時に自動再接続する", "Reconnect after disconnection"), settings.AutoReconnect);
            closeToTray = Check(T("閉じるボタンでタスクトレイに格納", "Keep running in the tray when closed"), settings.CloseToTray);
            oyasumiEnabled = Check(T("OyasumiVRの睡眠モードで睡眠ラベルを自動記録", "Record sleep labels from OyasumiVR sleep mode"), settings.OyasumiEnabled);
            oyasumiPort = Input(settings.OyasumiQueryPort.ToString());
            AutomationProperties.SetAutomationId(oyasumiEnabled, "OyasumiEnabled");
            AutomationProperties.SetAutomationId(oyasumiPort, "OyasumiQueryPort");
            sleepSettingsStatus = Label("", 12); AutomationProperties.SetAutomationId(sleepSettingsStatus, "OyasumiStatus");
            AutomationProperties.SetLiveSetting(sleepSettingsStatus, AutomationLiveSetting.Polite);
            themeChoice = Choice(new[] { T("Windowsに合わせる", "Follow Windows"), T("ダーク", "Dark"), T("ライト", "Light") }, settings.Theme == "Dark" ? 1 : settings.Theme == "Light" ? 2 : 0, 210);
            languageChoice = Choice(new[] { "日本語", "English" }, settings.Language == "en" ? 1 : 0, 210);
            AutomationProperties.SetAutomationId(languageChoice, "LanguageChoice"); AutomationProperties.SetAutomationId(themeChoice, "ThemeChoice");
            retentionChoice = Choice(new[] { T("無期限", "Unlimited"), T("1日（24時間）", "1 day (24 hours)"), T("7日", "7 days"), T("30日", "30 days"), T("90日", "90 days"), T("1年（365日）", "1 year (365 days)") }, Array.IndexOf(Settings.RetentionDayOptions, settings.RetentionDays), 230);
            AutomationProperties.SetAutomationId(retentionChoice, "RetentionChoice");
            var retentionHelp = Label("", 12); retentionHelp.Margin = new Thickness(0, 8, 0, 12);
            AutomationProperties.SetAutomationId(retentionHelp, "RetentionHelp");
            Action updateRetentionHelp = () => {
                int index = retentionChoice.SelectedIndex;
                if (index < 0) return;
                int days = Settings.RetentionDayOptions[index];
                retentionHelp.Visibility = days == 0 ? Visibility.Collapsed : Visibility.Visible;
                retentionHelp.Text = days == 0 ? "" :
                    T($"保存すると、{days}日より古い記録と、終了日時が期限以前の睡眠ラベルを自動削除します。既存データも対象で復元できません。必要なデータは変更前にバックアップしてください。", $"After saving, recordings older than {days} days and sleep labels ending at or before the cutoff are deleted automatically, including existing data. Deletion is irreversible. Back up needed data first.");
                retentionHelp.SetResourceReference(TextBlock.ForegroundProperty, days == 0 ? "TextMuted" : "Warning");
            };
            retentionChoice.SelectionChanged += (s, e) => updateRetentionHelp(); updateRetentionHelp();

            var portFields = new Grid(); portFields.ColumnDefinitions.Add(new ColumnDefinition()); portFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); portFields.ColumnDefinitions.Add(new ColumnDefinition());
            portFields.Children.Add(Field(T("送信先IPアドレス", "Destination IP address"), host)); var portField = Field(T("ポート", "Port"), port); Grid.SetColumn(portField, 2); portFields.Children.Add(portField);
            var conversionFields = new Grid(); conversionFields.ColumnDefinitions.Add(new ColumnDefinition()); conversionFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); conversionFields.ColumnDefinitions.Add(new ColumnDefinition());
            conversionFields.Children.Add(Field(T("最小 bpm → 0.0", "Minimum bpm → 0.0"), percentMin)); var maxField = Field(T("最大 bpm → 1.0", "Maximum bpm → 1.0"), percentMax); Grid.SetColumn(maxField, 2); conversionFields.Children.Add(maxField);
            var oscSection = Stack(Heading(T("送信先とパラメーター", "Destination & parameters")), oscEnabled, portFields,
                Field(T("Intパラメーター名", "Int parameter name"), intName), Field(T("Floatパラメーター名", "Float parameter name"), floatName));
            var fallbackFields = new Grid(); fallbackFields.ColumnDefinitions.Add(new ColumnDefinition()); fallbackFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); fallbackFields.ColumnDefinitions.Add(new ColumnDefinition());
            fallbackFields.Children.Add(Field(T("Int代替値（0〜255）", "Int fallback (0–255)"), oscTimeoutInt));
            var floatFallback = Field(T("Float代替値（0.0〜1.0）", "Float fallback (0.0–1.0)"), oscTimeoutFloat);
            Grid.SetColumn(floatFallback, 2); fallbackFields.Children.Add(floatFallback);
            var valuesSection = Stack(Heading(T("送信値と無受信時の動作", "Values & timeout behavior")),
                Label(T("心拍値の変換（Floatパラメーター）", "Heart-rate conversion (Float parameter)"), 12, "Text"), conversionFields,
                new Border { Height = 16 }, Field(T("無受信時の送信動作", "Transmission on receive timeout"), oscTimeoutMode), new Border { Height = 12 },
                Field(T("無受信時間（1〜3600秒）", "Receive timeout (1–3600 seconds)"), oscTimeoutSeconds), fallbackFields);
            var generalSection = Stack(Heading(T("一般", "General")), Field(T("テーマ", "Theme"), themeChoice), new Border { Height = 16 },
                Field(T("表示言語", "Language"), languageChoice), new Border { Height = 16 }, closeToTray);
            var storageLocation = Label(folder, 11, "TextMuted"); storageLocation.Margin = new Thickness(0, 4, 0, 0);
            var sensorSection = Stack(Heading(T("心拍センサー", "Heart-rate sensor")), autoConnect, autoReconnect);
            var oyasumiSection = Stack(Heading(T("OyasumiVR連携", "OyasumiVR integration")), oyasumiEnabled,
                Field(T("OSCQueryポート（0で自動検出）", "OSCQuery port (0 for automatic discovery)"), oyasumiPort),
                Label(T("同じPCのOyasumiVRでOSCを有効にしてください。2秒ごとに確認し、取得不能な間は睡眠に含めません。", "Enable OSC in OyasumiVR on this PC. Checks every 2 seconds; unavailable periods are excluded."), 12), sleepSettingsStatus);
            var storageSection = Stack(Heading(T("データ保存", "Data storage")), Field(T("保存期間", "Retention period"), retentionChoice), retentionHelp,
                new Border { Height = 18 }, Label(T("データ保存先", "Data location"), 12, "Text"), storageLocation);
            var sections = new Grid { VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetAutomationId(sections, "SettingsSections");
            settingsSections = new FrameworkElement[] { generalSection, sensorSection, storageSection, oyasumiSection, oscSection, valuesSection, (FrameworkElement)OscTestControls() };
            for (int index = 0; index < settingsSections.Length; index++) {
                var section = settingsSections[index];
                section.Visibility = index == settingsCategory ? Visibility.Visible : Visibility.Collapsed;
                AutomationProperties.SetAutomationId(section, "SettingsSection" + index);
                sections.Children.Add(section);
            }
            var form = Card(sections, new Thickness(24));
            var content = Stack(Heading(T("設定", "Settings")), new Border { Height = 8 }, form);
            content.MaxWidth = 800; content.HorizontalAlignment = HorizontalAlignment.Stretch;
            content.Margin = new Thickness(0, 0, 12, 0);
            var wrapper = new DockPanel(); var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            settingsStatus.Margin = new Thickness(2, 0, 16, 0); footer.Children.Add(settingsStatus);
            var saveButton = Button(T("保存して適用", "Save & apply"), SaveClicked, true, "SaveSettings", width: 168);
            saveButton.Margin = new Thickness(0); saveButton.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(saveButton, 1); footer.Children.Add(saveButton);
            DockPanel.SetDock(footer, Dock.Bottom); wrapper.Children.Add(footer);
            wrapper.Children.Add(content);
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            var navigation = CreateSettingsNavigation();
            layout.Children.Add(navigation); Grid.SetColumn(wrapper, 1); layout.Children.Add(wrapper);
            return layout;
        }
        private void SaveClicked(object sender, RoutedEventArgs e)
        {
            try {
                var next = settings.Copy(); int value;
                next.Host = host.Text.Trim(); if (!int.TryParse(port.Text, out value)) throw new ArgumentException(T("ポートは整数で入力してください。", "Port must be an integer.")); next.Port = value;
                next.IntParameter = intName.Text.Trim(); next.FloatParameter = floatName.Text.Trim();
                if (!int.TryParse(percentMin.Text, out value)) throw new ArgumentException(T("最小心拍数は整数で入力してください。", "Minimum heart rate must be an integer.")); next.PercentMin = value;
                if (!int.TryParse(percentMax.Text, out value)) throw new ArgumentException(T("最大心拍数は整数で入力してください。", "Maximum heart rate must be an integer.")); next.PercentMax = value;
                next.OscEnabled = oscEnabled.IsChecked == true; next.AutoConnect = autoConnect.IsChecked == true; next.AutoReconnect = autoReconnect.IsChecked == true; next.CloseToTray = closeToTray.IsChecked == true;
                next.OscZeroOnTimeout = oscTimeoutMode.SelectedIndex == 1;
                if (!int.TryParse(oscTimeoutSeconds.Text, out value)) throw new ArgumentException(T("無受信時間は整数で入力してください。", "Receive timeout must be an integer.")); next.OscTimeoutSeconds = value;
                if (!int.TryParse(oscTimeoutInt.Text, out value)) throw new ArgumentException(T("Int代替値は整数で入力してください。", "Int fallback must be an integer.")); next.OscTimeoutInt = value;
                float fallback;
                if (!float.TryParse(oscTimeoutFloat.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out fallback)) throw new ArgumentException(T("Float代替値は数値で入力してください。", "Float fallback must be a number.")); next.OscTimeoutFloat = fallback;
                next.Theme = new[] { "System", "Dark", "Light" }[themeChoice.SelectedIndex]; next.Language = languageChoice.SelectedIndex == 1 ? "en" : "ja";
                if (retentionChoice.SelectedIndex < 0) throw new ArgumentException(T("保存期間を選択してください。", "Choose a retention period."));
                next.RetentionDays = Settings.RetentionDayOptions[retentionChoice.SelectedIndex];
                next.OyasumiEnabled = oyasumiEnabled.IsChecked == true;
                if (!int.TryParse(oyasumiPort.Text, out value)) throw new ArgumentException(T("OSCQueryポートは整数で入力してください。", "OSCQuery port must be an integer."));
                next.OyasumiQueryPort = value;
                next.Validate(); next.Save(settingsPath); settings = next; engine.Configure(next); sleepMonitor.Configure(next); Words.Language = next.Language;
                BuildUi(); ApplyThemeTransition(true); tray?.Refresh(); Tick(); settingsStatus.Text = T("設定を保存・適用しました。", "Settings saved and applied.");
            } catch (Exception ex) { settingsStatus.Text = T("保存できません: ", "Could not save: ") + ex.Message; }
        }
        private static string RetentionNotice(int days) => days == 0 ? T("保存期間: 無期限", "Retention: unlimited") :
            T($"保存期間: {days}日（自動削除）", $"Retention: {days} days (automatic deletion)");
    }
}
