using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace HeartRateMonitor
{
    public sealed class App : Application
    {
        [STAThread]
        public static int Main(string[] args)
        {
            StartupOptions options;
            try { options = StartupOptions.Parse(args); }
            catch (Exception ex) {
                if (args != null && args.Contains("--smoke-test")) Console.Error.WriteLine(ex.Message);
                else MessageBox.Show(Words.T("起動できませんでした。\n", "Could not start.\n") + ex.Message, "Heart Rate Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            string data = options.DataDirectory, hash = options.InstanceKey;
            bool created;
            using (var mutex = new Mutex(true, "Local\\HeartRateMonitor-" + hash, out created)) {
                if (!created) {
                    try { if (InstanceNotification.TryRestore(hash)) return 0; }
                    catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
                    MessageBox.Show(Words.T("Heart Rate Monitorは起動済みです。タスクトレイから開いてください。", "Heart Rate Monitor is already running. Open it from the system tray."), "Heart Rate Monitor");
                    return 1;
                }
                try {
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceNotification.EventName(hash))) {
                        Directory.CreateDirectory(data);
                        var app = new App();
                        var window = new MainWindow(data);
                        if (options.SmokeTest) window.EnableSmokeTest(Path.Combine(data, "smoke.png"), options.Compact);
                        app.SessionEnding += (s, e) => window.EndSession();
                        var registration = ThreadPool.RegisterWaitForSingleObject(signal, (s, timedOut) => { if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(window.RestoreWindow)); }, null, -1, false);
                        try { app.Run(window); } finally { registration.Unregister(null); }
                    }
                    return 0;
                } catch (Exception ex) {
                    if (options.SmokeTest) {
                        Console.Error.WriteLine(ex.ToString());
                        try { File.WriteAllText(Path.Combine(data, "error.txt"), ex.ToString()); }
                        catch (Exception reportError) { Console.Error.WriteLine(reportError.Message); }
                    }
                    else MessageBox.Show(Words.T("起動できませんでした。\n", "Could not start.\n") + ex.Message, "Heart Rate Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
