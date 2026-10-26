using System.Windows.Automation;
using System.Windows.Controls;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private CheckBox oyasumiEnabled;
        private TextBox oyasumiPort;
        private TextBlock sleepSettingsStatus, sleepHistoryStatus;
        private bool sleepHistoryDirty;

        private void UpdateSleepStatus()
        {
            var status = sleepMonitor?.Status;
            string state = status?.State ?? (settings.OyasumiEnabled ? "Waiting" : "Disabled");
            string text = "OyasumiVR · " + (state == "Disabled" ? T("連携無効", "Integration disabled")
                : state == "Sleeping" ? T("睡眠モード有効 · 睡眠ラベルを自動記録中", "Sleep mode on · recording sleep labels")
                : state == "Awake" ? T("睡眠モード無効", "Sleep mode off")
                : state == "Waiting" ? T("検出待ち · OyasumiVRのOSCを有効にしてください", "Waiting · enable OSC in OyasumiVR")
                : state == "Unavailable" ? T("状態を取得できません · 睡眠は記録しません", "State unavailable · sleep is not recorded")
                : T("連携・保存エラー · 睡眠は記録しません", "Integration/storage error · sleep is not recorded"));
            foreach (var label in new[] { sleepSettingsStatus, sleepHistoryStatus }) if (label != null) {
                label.Text = text; label.ToolTip = text + (string.IsNullOrEmpty(status?.Detail) ? "" : "\n" + status.Detail);
                label.SetResourceReference(TextBlock.ForegroundProperty, state == "Error" || state == "Unavailable" ? "Warning" : state == "Sleeping" ? "Accent" : "TextMuted");
            }
        }
    }
}
