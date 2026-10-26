using System;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private void BindConnectionEvents()
        {
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
        }

        private void BindRecordingEvents()
        {
            engine.Error += message => {
                connectionLog.Add("Recording: " + message);
                Ui(() => errorText.Text = T("記録エラー: ", "Recording error: ") + message);
            };
            if (engine.StartupRetentionError != null) {
                string retentionError = T("古い記録の削除に失敗しました: ", "Could not delete expired recordings: ") + engine.StartupRetentionError;
                connectionLog.Add(retentionError);
                errorText.Text = retentionError;
            }
            engine.RetentionChanged += () => Ui(() => { if (tabs.SelectedIndex == 1) _ = RefreshHistory(false); });
            engine.OscFailed += message => {
                connectionLog.Add("OSC: " + message);
                Ui(() => { oscFailure = message; UpdateOsc(); });
            };
            engine.Transmitted += value => Ui(() => { transmission = value; oscFailure = null; UpdateOsc(); });
            engine.Processed += (row, saved, osc) => Ui(() => Received(row, saved, osc));
        }
    }
}
