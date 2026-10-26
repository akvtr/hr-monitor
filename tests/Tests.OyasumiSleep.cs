using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestOyasumiSleep(string folder)
    {
        Check(new Settings().OyasumiEnabled && new Settings().OyasumiQueryPort == 0, "Oyasumi integration defaults to enabled with automatic local discovery");
        var settings = new Settings { OyasumiEnabled = false, OyasumiQueryPort = 23456 };
        string settingsPath = Path.Combine(folder, "oyasumi-settings.json"); settings.Save(settingsPath);
        var reloaded = Settings.Load(settingsPath);
        Check(!reloaded.OyasumiEnabled && reloaded.OyasumiQueryPort == 23456 && reloaded.Copy().OyasumiQueryPort == 23456, "Oyasumi preferences persist and are copied");
        Throws<ArgumentException>(() => new Settings { OyasumiQueryPort = -1 }.Validate(), "Oyasumi rejects negative query ports");
        Throws<ArgumentException>(() => new Settings { OyasumiQueryPort = 65536 }.Validate(), "Oyasumi rejects out of range query ports");
        Check(typeof(MainWindow).GetMethod("CreateAnnotationDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) == null,
            "manual note and interval editing UI is removed");

        string database = Path.Combine(folder, "oyasumi-intervals.db");
        ExecuteIsolatedSql(database, "INSERT INTO readings(utc_ms,session,device,bpm,rr_ms) VALUES(1000,'existing','sensor',70,'');");
        ExecuteIsolatedSql(database, "CREATE TABLE annotations(id INTEGER PRIMARY KEY,from_ms INTEGER,to_ms INTEGER,label TEXT,memo TEXT);");
        ExecuteIsolatedSql(database, "INSERT INTO annotations VALUES(1,0,999999,'old manual note','preserve as legacy');");
        using (var store = new Store(database)) {
            Check(store.SleepIntervals(0, 1000000).Count == 0, "legacy manual annotations are never relabeled as automatic sleep");
            var tracker = new SleepIntervalTracker(store);
            tracker.Observe(false, 0); tracker.Observe(true, 1000);
            Check(store.SleepIntervals(0, 6000).Count == 0, "a single sleep observation does not fabricate a nonzero interval");
            tracker.Observe(true, 3000); tracker.Observe(false, 5000); tracker.Observe(false, 7000);
            var first = store.SleepIntervals(0, 10000).Single();
            Check(first.From == 1000 && first.To == 5000, "sleep mode enables and disables one persistent interval without duplicating repeated states");
            tracker.Observe(true, 8000); tracker.Observe(true, 9000); tracker.Observe(null, 9500);
            tracker.Observe(true, 12000); tracker.Observe(true, 14000); tracker.Observe(false, 16000);
            var intervals = store.SleepIntervals(0, 20000);
            Check(intervals.Count == 3 && intervals[1].To == 9000 && intervals[2].From == 12000, "unknown communication gaps are excluded from sleep labels after reconnecting");
            tracker.Observe(true, 20000); tracker.Observe(true, 21000); tracker.Observe(true, 40000); tracker.Observe(true, 41000);
            intervals = store.SleepIntervals(0, 50000);
            Check(intervals.Count == 5 && intervals[3].To == 21000 && intervals[4].From == 40000, "PC suspension or a long polling gap starts a separate sleep interval");
            tracker = new SleepIntervalTracker(store); tracker.Observe(true, 42000); tracker.Observe(false, 43000);
            Check(store.SleepIntervals(0, 50000).Count == 6, "restarting tracking never extends a previous run across unobserved time");
            Check(store.Query(0, 50000).Single().Bpm == 70, "sleep observations neither fabricate nor change heart-rate readings");
            Check(store.SleepIntervals(5000, 8000).Count == 0 && store.SleepIntervals(4999, 5001).Count == 1, "sleep labels use half-open overlap boundaries");
            Check(store.SleepIntervals(2000, 2000).Count == 0, "empty sleep query ranges contain no intervals");
            Check(store.SleepIntervals(3000, 2000).Count == 0, "reversed sleep query ranges contain no intervals");
            ExecuteIsolatedSql(database, "CREATE TRIGGER reject_sleep_prune BEFORE DELETE ON readings BEGIN SELECT RAISE(ABORT,'ISOLATED failure'); END;");
            Throws<IOException>(() => store.DeleteBefore(10000), "sleep retention reports reading deletion failures");
            Check(store.SleepIntervals(0, 50000).Count == 6, "sleep retention rolls back label deletion when reading deletion fails");
            ExecuteIsolatedSql(database, "DROP TRIGGER reject_sleep_prune;");
            Check(store.DeleteBefore(10000) == 1 && store.SleepIntervals(0, 50000).Count == 4, "finite retention deletes ended sleep labels and old readings atomically");
        }
        using (var store = new Store(database))
            Check(store.SleepIntervals(0, 50000).Count == 4, "automatic sleep intervals survive database reopening");

        string boundsDatabase = Path.Combine(folder, "oyasumi-history-bounds.db");
        var history = new HistoryRepository(boundsDatabase);
        using (var store = new Store(boundsDatabase)) {
            store.SaveSleepInterval(new SleepInterval { From = 500, To = 500 });
            Check(history.LoadBounds() == null, "zero-duration sleep observations do not create navigable history");
            var sleep = new SleepInterval { From = 1000, To = 5000 };
            store.SaveSleepInterval(sleep);
            var bounds = history.LoadBounds();
            Check(bounds.From == 1000 && bounds.To == 5000 && history.LoadWindow(bounds).Points.Count == 0,
                "sleep-only history is navigable without fabricated heart-rate samples");
            store.Add(new Reading { UnixMs = 9000, Bpm = 70 });
            sleep.To = 12000; store.SaveSleepInterval(sleep);
            var window = history.LoadSleepWindow(new TimeWindow(8000, 13000));
            Check(window.Bounds.From == 1000 && window.Bounds.To == 12000 && window.Intervals.Single().To == 12000,
                "lightweight sleep refresh includes intervals outside the heart-rate bounds");
            Check(history.LoadBounds().To == 12000 && history.LoadWindow(new TimeWindow(8000, 13000)).Summary.Count == 1,
                "combined history bounds preserve real heart-rate statistics");
        }

        const string prefix = "{\"FULL_PATH\":\"/OyasumiVR/SleepMode\",\"ACCESS\":3,\"VALUE_TYPE\":\"F\",\"VALUE\":";
        Check(OyasumiOscQuery.ParseSleep(prefix + "[true]}") && !OyasumiOscQuery.ParseSleep(prefix + "[false]}"), "official OSCQuery Boolean VALUE is read independently of its static false type tag");
        foreach (string invalid in new[] { "{}", prefix + "[1]}", prefix + "[\"true\"]}", prefix + "[]}", prefix.Replace("\"ACCESS\":3", "\"ACCESS\":2") + "[true]}" })
            Throws<InvalidDataException>(() => OyasumiOscQuery.ParseSleep(invalid), "invalid or write-only sleep mode JSON cannot imply sleep");
        using (var server = new OyasumiTestServer()) {
            Check(OyasumiPorts.FindForProcessIds(new HashSet<int> { System.Diagnostics.Process.GetCurrentProcess().Id }).Contains(server.Port),
                "native local TCP discovery finds the correct listener port owned by a known process");
            var client = new OyasumiOscQuery();
            server.Sleeping = true;
            Check(client.Read(server.Port, CancellationToken.None), "OSCQuery client confirms OyasumiVR identity and reads enabled sleep mode over loopback HTTP");
            server.Sleeping = false;
            Check(!client.Read(server.Port, CancellationToken.None), "OSCQuery client reads sleep mode disabling");
            server.Name = "UnrelatedServer";
            Throws<InvalidDataException>(() => client.Read(server.Port, CancellationToken.None), "a different OSCQuery server cannot label sleep");
            server.Name = "OyasumiVR";
            using (var canceled = new CancellationTokenSource()) {
                canceled.Cancel(); Throws<OperationCanceledException>(() => client.Read(server.Port, canceled.Token), "canceled sleep queries return without network work");
            }
            TestOyasumiMonitor(folder, server, client);
        }
        TestOyasumiSettingsUi(folder);
        using (var slow = new OyasumiTestServer { SlowBody = true }) {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            Throws<TimeoutException>(() => new OyasumiOscQuery().Read(slow.Port, CancellationToken.None), "trickling OSCQuery deadline is reported as a failure rather than shutdown cancellation");
            Check(elapsed.ElapsedMilliseconds < 4000, "slow response cannot monopolize the sleep polling worker indefinitely");
        }
        using (var slow = new OyasumiTestServer { SlowBody = true })
        using (var cancel = new CancellationTokenSource(100)) {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            Throws<OperationCanceledException>(() => new OyasumiOscQuery().Read(slow.Port, cancel.Token), "in-flight OSCQuery reads are canceled during shutdown or reconfiguration");
            Check(elapsed.ElapsedMilliseconds < 1500, "canceling an active sleep request promptly interrupts blocking reads");
        }
    }

    private static void TestOyasumiDiscoveryCancellation(string folder)
    {
        foreach (string action in new[] { "stop", "disable", "active" }) {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim()) {
                string database = Path.Combine(folder, "oyasumi-discovery-" + action + ".db");
                var settings = new Settings { OyasumiEnabled = true };
                int errors = 0;
                var monitor = new OyasumiSleepMonitor(database, settings, () => {
                    entered.Set();
                    if (!release.Wait(3000)) throw new TimeoutException("ISOLATED discovery was not released");
                    throw new System.ComponentModel.Win32Exception(5, "ISOLATED listener discovery failure");
                }, interval: TimeSpan.FromMilliseconds(10));
                monitor.StatusChanged += status => { if (status.State == "Error") Interlocked.Increment(ref errors); };
                try {
                    monitor.Start();
                    Check(entered.Wait(3000), "Oyasumi discovery is pending before " + action);
                    Task stopped = null;
                    if (action == "stop") stopped = monitor.StopAsync();
                    if (action == "disable") { settings.OyasumiEnabled = false; monitor.Configure(settings); }
                    release.Set();
                    if (stopped != null) Check(stopped.Wait(3000), "Oyasumi shutdown drains the failed discovery");
                    else WaitUntil(() => action == "disable" ? monitor.Status.State == "Disabled" : Volatile.Read(ref errors) > 0);
                    Check(action == "active" ? Volatile.Read(ref errors) > 0 : Volatile.Read(ref errors) == 0,
                        "Oyasumi reports discovery errors only for the current enabled operation after " + action);
                    Check(!File.Exists(database), "failed Oyasumi discovery creates no observations after " + action);
                } finally { release.Set(); Check(monitor.StopAsync().Wait(3000), "Oyasumi discovery regression worker exits after " + action); }
            }
        }
    }

    private static void TestOyasumiSettingsUi(string folder)
    {
        RunOnSta(() => {
            MainWindow window = null;
            try {
                string directory = Path.Combine(folder, "oyasumi-settings-ui");
                string path = Path.Combine(directory, "settings.json");
                new Settings { AutoConnect = false, CloseToTray = false, OscEnabled = false }.Save(path);
                window = new MainWindow(directory);
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var port = (System.Windows.Controls.TextBox)typeof(MainWindow).GetField("oyasumiPort", flags).GetValue(window);
                var enabled = (System.Windows.Controls.CheckBox)typeof(MainWindow).GetField("oyasumiEnabled", flags).GetValue(window);
                var save = typeof(MainWindow).GetMethod("SaveClicked", flags);
                byte[] original = File.ReadAllBytes(path); port.Text = "-1";
                save.Invoke(window, new object[] { null, null });
                Check(File.ReadAllBytes(path).SequenceEqual(original), "invalid Oyasumi UI port leaves saved preferences unchanged");
                port.Text = "23456"; enabled.IsChecked = false; save.Invoke(window, new object[] { null, null });
                var saved = Settings.Load(path);
                Check(!saved.OyasumiEnabled && saved.OyasumiQueryPort == 23456, "settings UI saves integration disable and OSCQuery port together");
            } finally { if (window != null) { window.EndSession(); window.Close(); } }
        }, "Oyasumi settings UI verification failed");
    }

    private static void TestOyasumiMonitor(string folder, OyasumiTestServer server, OyasumiOscQuery client)
    {
        string database = Path.Combine(folder, "oyasumi-monitor.db");
        int port = server.Port; bool missing = false;
        var configuration = new Settings { OyasumiEnabled = true, OyasumiQueryPort = 0 };
        var monitor = new OyasumiSleepMonitor(database, configuration,
            () => missing ? new List<int>() : new List<int> { port }, client.Read, TimeSpan.FromMilliseconds(40));
        server.Sleeping = true;
        try {
            monitor.Start(); monitor.Start();
            Check(SpinWait.SpinUntil(() => monitor.Status.State == "Sleeping", 3000), "automatic Oyasumi discovery starts polling and recognizes sleep");
            Check(SpinWait.SpinUntil(() => { using (var store = new Store(database)) return store.SleepIntervals(0, long.MaxValue).Count > 0; }, 3000), "sleep mode persists an interval while it remains enabled");
            server.Name = "UnrelatedServer"; missing = true;
            Check(SpinWait.SpinUntil(() => monitor.Status.State == "Waiting", 3000), "Oyasumi disappearance reports waiting instead of assuming sleep or wakefulness");
            long before;
            using (var store = new Store(database)) before = store.SleepIntervals(0, long.MaxValue).Single().To;
            server.Name = "OyasumiVR"; missing = false;
            Check(SpinWait.SpinUntil(() => monitor.Status.State == "Sleeping", 3000), "Oyasumi reconnects automatically");
            Check(SpinWait.SpinUntil(() => { using (var store = new Store(database)) return store.SleepIntervals(0, long.MaxValue).Count == 2; }, 3000), "reconnection creates a new sleep label without spanning an unknown gap");
            using (var store = new Store(database)) Check(store.SleepIntervals(0, long.MaxValue)[0].To == before, "reconnecting never extends the previous confirmed interval");
            server.Sleeping = false;
            Check(SpinWait.SpinUntil(() => monitor.Status.State == "Awake", 3000), "sleep mode disabling stops automatic labeling");
            configuration.OyasumiEnabled = false; monitor.Configure(configuration);
            Check(SpinWait.SpinUntil(() => monitor.Status.State == "Disabled", 3000), "saved integration disable is applied to the running monitor");
        } finally { Check(monitor.StopAsync().Wait(3000), "Oyasumi polling shuts down and drains its persistent work"); }
    }

    private sealed class OyasumiTestServer : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly Task worker;
        private volatile bool stopping;
        internal volatile bool Sleeping;
        internal volatile bool SlowBody;
        internal volatile string Name = "OyasumiVR";
        internal int Port { get; }
        internal OyasumiTestServer()
        {
            listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            worker = Task.Run(() => {
                while (!stopping) {
                    try {
                        using (var connection = listener.AcceptTcpClient())
                        using (var stream = connection.GetStream()) {
                            stream.ReadTimeout = 2000; stream.WriteTimeout = 2000;
                            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                            string first = reader.ReadLine(); string line;
                            while (!string.IsNullOrEmpty(line = reader.ReadLine())) { }
                            string json = first != null && first.Contains("HOST_INFO") ? "{\"NAME\":\"" + Name + "\"}"
                                : "{\"FULL_PATH\":\"/OyasumiVR/SleepMode\",\"ACCESS\":3,\"VALUE\":[" + (Sleeping ? "true" : "false") + "]}";
                            byte[] body = Encoding.UTF8.GetBytes(json);
                            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                            stream.Write(header, 0, header.Length);
                            if (SlowBody) {
                                foreach (byte value in body) { if (stopping) break; stream.WriteByte(value); stream.Flush(); Thread.Sleep(100); }
                            } else stream.Write(body, 0, body.Length);
                        }
                    } catch (SocketException) { if (stopping) break; }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { if (stopping) break; }
                }
            });
        }
        public void Dispose() { stopping = true; listener.Stop(); worker.Wait(3000); }
    }
}
