using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace HeartRateMonitor
{
    public sealed class App : Application
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeartRateMonitor");
            int index = Array.IndexOf(args, "--data-dir");
            if (index >= 0 && index + 1 < args.Length) data = Path.GetFullPath(args[index + 1]);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToUpperInvariant()))).Replace("-", "");
            bool created;
            using (var mutex = new Mutex(true, "Local\\HeartRateMonitor-" + hash, out created)) {
                if (!created) {
                    try { using (var signal = EventWaitHandle.OpenExisting("Local\\HeartRateMonitor-Show-" + hash)) signal.Set(); return 0; }
                    catch { MessageBox.Show("Heart Rate Monitor is already running. Open it from the system tray.", "Heart Rate Monitor"); return 1; }
                }
                try {
                    Directory.CreateDirectory(data);
                    var app = new App();
                    var window = new MainWindow(data);
                    if (args.Contains("--smoke-test")) window.EnableSmokeTest(Path.Combine(data, "smoke.png"), args.Contains("--compact"));
                    app.SessionEnding += (s, e) => window.EndSession();
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\HeartRateMonitor-Show-" + hash)) {
                        var registration = ThreadPool.RegisterWaitForSingleObject(signal, (s, timedOut) => { if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(window.RestoreWindow)); }, null, -1, false);
                        try { app.Run(window); } finally { registration.Unregister(null); }
                    }
                    return 0;
                } catch (Exception ex) {
                    if (args.Contains("--smoke-test")) File.WriteAllText(Path.Combine(data, "error.txt"), ex.ToString());
                    else MessageBox.Show(Words.T("起動できませんでした。\n", "Could not start.\n") + ex.Message, "Heart Rate Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
