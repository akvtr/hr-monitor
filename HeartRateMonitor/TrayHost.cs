using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace HeartRateMonitor
{
    public sealed class TrayHost : IDisposable
    {
        private readonly Forms.NotifyIcon tray;
        private readonly Icon icon;
        private readonly Action show, start, stop, exit;
        public TrayHost(Action show, Action start, Action stop, Action exit)
        {
            this.show = show; this.start = start; this.stop = stop; this.exit = exit;
            icon = ProductIcon.CreateTrayIcon();
            tray = new Forms.NotifyIcon { Icon = icon, Text = "Heart Rate Monitor", Visible = true };
            tray.DoubleClick += (s, e) => show(); Refresh();
        }
        public void Refresh()
        {
            var old = tray.ContextMenuStrip;
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(Words.T("ウィンドウを開く", "Open window"), null, (s, e) => show());
            menu.Items.Add(Words.T("自動接続", "Connect automatically"), null, (s, e) => start());
            menu.Items.Add(Words.T("接続を停止", "Stop connection"), null, (s, e) => stop());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Words.T("終了", "Exit"), null, (s, e) => exit());
            tray.ContextMenuStrip = menu; old?.Dispose();
        }
        public void SetStatus(string status) { var text = "Heart Rate Monitor · " + status; tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; }
        public void ExplainHidden() { tray.ShowBalloonTip(2500, "Heart Rate Monitor", Words.T("記録とOSC送信は継続します。アイコンをダブルクリックして開けます。終了は右クリックメニューから。", "Recording and OSC continue. Double-click the tray icon to reopen; right-click to exit."), Forms.ToolTipIcon.Info); }
        public void Dispose() { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); icon.Dispose(); }
    }
}
