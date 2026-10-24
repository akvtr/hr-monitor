using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeartRateMonitor;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

internal static class Tests
{
    private const long DayMilliseconds = 86400000L;
    private static int count;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    private static void Throws(Action action, string name) { try { action(); } catch (Exception) { count++; Console.WriteLine("PASS " + name); return; } throw new Exception("Expected rejection: " + name); }
    private static int Bits(byte[] packet) { int n = packet.Length; return (packet[n - 4] << 24) | (packet[n - 3] << 16) | (packet[n - 2] << 8) | packet[n - 1]; }
    private static string Address(byte[] packet) => Encoding.ASCII.GetString(packet, 0, Array.IndexOf(packet, (byte)0));
    private static char Type(byte[] packet) { int offset = (Array.IndexOf(packet, (byte)0) + 4) / 4 * 4; return (char)packet[offset + 1]; }
    private static void TestHistorySelection()
    {
        foreach (long width in new long[] { 60000, 600000, 1800000, 3600000, 86400000, 365L * 86400000 })
            Check(TimeSpanPicker.Decode(TimeSpanPicker.Encode(width), 365L * 86400000) == width, "time-span slider round-trips " + width);
        Check(TimeSpanPicker.Decode(-5, 3600000) == 60000, "time-span slider respects one-minute minimum");
        Check(TimeSpanPicker.Decode(30, 7123456) == 7123456, "time-span slider reaches exact recording boundary");
        Check(TimeSpanPicker.Decode(TimeSpanPicker.Encode(7140001), 7140001) == 7140001, "slider endpoint includes final sample without rounding down");
        var domain = new TimeWindow(0, 86400000);
        var range = new TimeWindow(1200000, 1800000);
        var moved = HistorySelection.Move(domain, range, -86400000);
        Check(moved.From == 0 && moved.To == 600000, "overview pan clamps left while preserving width");
        moved = HistorySelection.Move(domain, range, 86400000);
        Check(moved.From == 85800000 && moved.To == domain.To, "overview pan clamps right while preserving width");
        var resized = HistorySelection.Resize(domain, range, true, 3600000);
        Check(resized.From == range.To - 60000 && resized.To == range.To, "left range handle cannot cross right handle");
        resized = HistorySelection.Resize(domain, range, false, 0);
        Check(resized.From == range.From && resized.To == range.From + 60000, "right range handle cannot cross left handle");
        resized = HistorySelection.Resize(domain, range, false, long.MaxValue);
        Check(resized.To == domain.To && resized.From == range.From, "range zoom clamps to retained data");
        var shortened = HistorySelection.Clamp(new TimeWindow(1500000, 1700000), range);
        Check(shortened.From == 1500000 && shortened.To == 1700000, "retention shrinking the domain clamps both selection boundaries");
        var single = HistorySelection.Clamp(new TimeWindow(1000, 1001), range);
        Check(single.From == 1000 && single.To == 1001, "single reading selection remains valid");
        var oversized = HistorySelection.Clamp(domain, new TimeWindow(-60000, 90000000));
        Check(oversized.From == 0 && oversized.To == domain.To, "all-data preset covers available domain");
        var points = new List<ChartPoint>();
        for (int i = 0; i <= 3600; i++) points.Add(new ChartPoint { UnixMs = i * 1000L, Bpm = 60 + i % 20, Min = 50, Max = 100 });
        var preview = HistorySelection.Preview(points, new TimeWindow(600000, 1200000), 100);
        Check(preview.Count <= 100 && preview.All(p => p.UnixMs >= 600000 && p.UnixMs < 1200000), "drag preview is bounded and respects half-open selected range");
        Check(preview.All(p => p.Min == 50 && p.Max == 100 && p.Bpm >= 60 && p.Bpm <= 79), "drag preview preserves recorded extrema");
        Check(points[600].Bpm == 60 && points.Count == 3601, "drag preview leaves cached measurements unchanged");
        Check(HistorySelection.Preview(points, new TimeWindow(4000000, 4600000)).Count == 0, "empty selected period never fabricates points");
    }
    private static void TestChartInteraction()
    {
        var domain = new TimeWindow(0, 7200000); var view = new TimeWindow(1800000, 2400000);
        var zoom = HistorySelection.Zoom(domain, view, 120, 0.5);
        Check(zoom.To - zoom.From == 480000 && zoom.From + (zoom.To - zoom.From) / 2 == 2100000, "wheel zoom narrows range around cursor timestamp");
        Check(HistorySelection.Zoom(domain, view, 120, 0).From == view.From && HistorySelection.Zoom(domain, view, 120, 1).To == view.To, "wheel zoom preserves left and right cursor anchors");
        zoom = HistorySelection.Zoom(domain, view, -120, 0.5);
        Check(zoom.To - zoom.From == 750000, "reverse wheel broadens range");
        zoom = HistorySelection.Zoom(domain, view, 120000, 0.5);
        Check(zoom.To - zoom.From == 60000, "extreme zoom in clamps to one minute");
        zoom = HistorySelection.Zoom(domain, view, -120000, 0.5);
        Check(zoom.From == domain.From && zoom.To == domain.To, "extreme zoom out clamps to retained domain");
        var minimum = new TimeWindow(100000, 160000); zoom = HistorySelection.Zoom(domain, minimum, 120, 0.5);
        Check(zoom.From == minimum.From && zoom.To == minimum.To, "minimum zoom does not move selected window");
        var selected = HistorySelection.SelectedInterval(view, 0.8, 0.2);
        Check(selected.From == 1920000 && selected.To == 2280000, "reverse chart drag selects ordered timestamp interval");
        Check(HistorySelection.SelectedInterval(view, 0.5, 0.5) == null, "click without interval never fabricates a selection");
        selected = HistorySelection.SelectedInterval(view, -1, 2);
        Check(selected.From == view.From && selected.To == view.To, "chart drag clamps outside pointer positions to visible bounds");
        var samples = new List<ChartPoint> {
            new ChartPoint { UnixMs=0, Bpm=60, Min=60, Max=60 }, new ChartPoint { UnixMs=10, Bpm=90, Min=90, Max=90 },
            new ChartPoint { UnixMs=15, Bpm=0, Min=0, Max=0 }, new ChartPoint { UnixMs=20, Bpm=120, Min=120, Max=120 }
        };
        var summary = ChartAnalysis.SummarizeRaw(samples, new TimeWindow(0,20));
        Check(summary.Count == 2 && summary.Average == 75 && summary.Min == 60 && summary.Max == 90, "interval stats use original valid samples and half-open range");
        summary = ChartAnalysis.SummarizeRaw(samples, new TimeWindow(30,40));
        Check(summary.Count == 0 && summary.Average == 0, "empty analysis interval has no invented average");
        Throws(() => ChartAnalysis.SummarizeRaw(new[] { new ChartPoint { UnixMs=0, Bpm=75, Min=60, Max=90 } }, new TimeWindow(0,10)), "interval stats reject averaged chart buckets");
    }
    private static void TestDailyActivity(string folder)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-jst", TimeSpan.FromHours(9), "test-jst", "test-jst");
        var first = new DateTime(2024, 2, 28);
        long midnight = DailyActivity.Midnight(first.AddDays(1), zone);
        using (var store = new Store(Path.Combine(folder, "activity.db"))) {
            foreach (long time in new[] { midnight - 1, midnight, midnight + 1, midnight + 86400000 })
                store.Add(new Reading { UnixMs = time, Bpm = 80, Session = "isolated", Device = "test" });
            store.Add(new Reading { UnixMs = midnight + 2, Bpm = 0 });
            store.Add(new Reading { UnixMs = midnight + 3, Bpm = 90, Contact = false });
            var days = store.DailyActivity(first, first.AddDays(3), zone);
            Check(days.Count == 4 && days[0].Count == 1 && days[1].Count == 2 && days[2].Count == 1 && days[3].Count == 0, "calendar groups local midnight boundaries, leap day and empty days without invalid samples");
            Check(days[1].Date == new DateTime(2024, 2, 29), "calendar preserves local leap-day date");
            Check(store.DailyActivity(first.AddDays(-364), first, zone).Count == 365, "calendar returns exactly 365 days including empty days");
            Throws(() => store.DailyActivity(first, first.AddDays(-1), zone), "calendar rejects reversed range");
            store.DeleteBefore(midnight + 1);
            Check(store.DailyActivity(first, first.AddDays(1), zone).Sum(d => d.Count) == 1, "calendar reflects retention deletion without cached counts");
        }
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var spring = new DateTime(2024, 3, 10); var autumn = new DateTime(2024, 11, 3);
        Check(DailyActivity.Midnight(spring.AddDays(1), pacific) - DailyActivity.Midnight(spring, pacific) == 23 * 3600000L, "calendar respects 23-hour daylight-saving day");
        Check(DailyActivity.Midnight(autumn.AddDays(1), pacific) - DailyActivity.Midnight(autumn, pacific) == 25 * 3600000L, "calendar respects 25-hour daylight-saving day");
        Check(new long[] { 0, 1, 599, 600, 3599, 3600, 10799, 10800 }.Select(DailyActivity.Level).SequenceEqual(new[] { 0, 1, 1, 2, 2, 3, 3, 4 }), "calendar color thresholds are fixed sample counts");
    }
    private static void TestActivityCalendarUi()
    {
        Exception failure = null;
        var thread = new Thread(() => {
            try {
                var host = new System.Windows.Window(); ThemeManager.Initialize(host); ThemeManager.Apply(host, "Dark");
                var calendar = new ActivityCalendar(); host.Content = calendar;
                var start = new DateTime(2025, 10, 25);
                var activity = Enumerable.Range(0, 365).Select(i => new DailyActivity { Date = start.AddDays(i), Count = i == 0 ? 600 : 0 }).ToList();
                calendar.SetDays(activity);
                var grid = calendar.Children.OfType<System.Windows.Controls.Grid>().Single();
                var buttons = grid.Children.OfType<System.Windows.Controls.Button>().ToList();
                Check(buttons.Count == 365 && buttons.Count(b => b.IsTabStop) == 1, "calendar exposes every day with a single keyboard tab entry");
                Check(System.Windows.Automation.AutomationProperties.GetAutomationId(buttons[0]) == "ActivityDay20251025", "calendar exposes exact dates to automation");
                Check(grid.Children.OfType<System.Windows.Controls.TextBlock>().Any(label => label.Text == "11月"), "short first month does not suppress the next month label");
                DateTime? selected = null; calendar.DaySelected += date => selected = date;
                buttons[0].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(selected == start, "calendar click navigates to the matching recorded date");
                buttons[1].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(selected == start.AddDays(1), "calendar empty-day click preserves the requested date");
                ThemeManager.Apply(host, "Light");
                Check(buttons[0].Background == host.Resources["Activity2"], "calendar day colors follow shared theme resources");
                calendar.SetError("isolated error");
                Check(grid.Children.Count == 0, "calendar read failure clears stale day values");
                host.Close();
            } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception("Calendar UI verification failed", failure);
    }
    public static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hr-monitor-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try {
            TestAutoConnection();
            TestGattAccess();
            TestPresentationAndOsc(folder);
            TestRecordingPipeline(folder);
            TestHistorySelection();
            TestChartInteraction();
            TestDailyActivity(folder);
            TestActivityCalendarUi();
            TestRetentionPolicy(folder);
            var m = Measurement.Parse(new byte[] { 0, 72 }); Check(m.Bpm == 72 && m.Contact == null, "8-bit heart rate and unsupported contact");
            m = Measurement.Parse(new byte[] { 1, 44, 1 }); Check(m.Bpm == 300, "16-bit little-endian heart rate");
            m = Measurement.Parse(new byte[] { 30, 80, 42, 0, 0, 4, 0, 2 });
            Check(m.Bpm == 80 && m.Contact == true && m.Energy == 42 && m.RrMilliseconds.SequenceEqual(new[] { 1000.0, 500.0 }), "contact, energy and multiple RR intervals");
            Check(Measurement.Parse(new byte[] { 4, 0 }).Contact == false, "absent contact");
            Throws(() => Measurement.Parse(new byte[] { 1, 3 }), "truncated 16-bit measurement");
            Throws(() => Measurement.Parse(new byte[] { 16, 80, 1 }), "truncated RR interval");
            Throws(() => Measurement.Parse(new byte[] { 16, 80 }), "missing RR interval");
            Throws(() => Measurement.Parse(new byte[] { 8, 80, 1 }), "truncated energy");
            Throws(() => Measurement.Parse(new byte[] { 0, 80, 1 }), "unexpected trailing bytes");
            Throws(() => Measurement.Parse(null), "null measurement");

            var settings = new Settings(); settings.Validate();
            Check(settings.AutoConnect && settings.Theme == "System", "automatic connection and system theme defaults");
            string oldConfig = Path.Combine(folder, "old-settings.json"); File.WriteAllText(oldConfig, "{\"Port\":9000}");
            Check(Settings.Load(oldConfig).AutoConnect && Settings.Load(oldConfig).Theme == "System", "old settings migrate without disabling auto-connect");
            var badTheme = settings.Copy(); badTheme.Theme = "invalid"; Throws(badTheme.Validate, "invalid theme rejected");
            Check(Settings.Address(settings.IntParameter) == "/avatar/parameters/VRCOSC/Heartrate/Value", "default Int OSC address");
            Check(Settings.Address(settings.FloatParameter) == "/avatar/parameters/HRPercent", "default Float OSC address");
            Check(Settings.Address("/avatar/parameters/HRPercent") == "/avatar/parameters/HRPercent", "full address not duplicated");
            Check(settings.Percent(100) == 0.5f && settings.Percent(250) == 1f && settings.Percent(-1) == 0, "percent default and clamp");
            var custom = settings.Copy(); custom.PercentMin = 40; custom.PercentMax = 180; Check(custom.Percent(110) == 0.5f, "custom percent range");
            custom.PercentMax = 40; Throws(custom.Validate, "invalid conversion interval");
            foreach (var invalid in new[] { "", "/input/Jump", "HR Percent", "HR*", "HR//Value", "心拍", "HR\0" }) Throws(() => Settings.Address(invalid), "invalid parameter: " + invalid);
            custom = settings.Copy(); custom.FloatParameter = custom.IntParameter; Throws(custom.Validate, "conflicting OSC parameters");
            custom = settings.Copy(); custom.Port = 0; Throws(custom.Validate, "invalid UDP port");
            custom = settings.Copy(); custom.Host = "not-an-ip"; Throws(custom.Validate, "invalid destination");
            string config = Path.Combine(folder, "settings.json"); settings.Save(config); settings.Port = 9010; settings.Save(config);
            Check(Settings.Load(config).Port == 9010 && File.Exists(config + ".bak"), "atomic settings replacement and backup");

            var packet = OscSender.Packet("/x", 'i', 72);
            Check(packet.SequenceEqual(new byte[] { 47, 120, 0, 0, 44, 105, 0, 0, 0, 0, 0, 72 }), "OSC byte-for-byte encoding and alignment");
            using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var sender = new OscSender()) {
                settings.Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port; receiver.Client.ReceiveTimeout = 2000;
                sender.Send(settings, 100); IPEndPoint remote = null;
                var a = receiver.Receive(ref remote); var b = receiver.Receive(ref remote);
                Check(Address(a) == "/avatar/parameters/VRCOSC/Heartrate/Value" && Type(a) == 'i' && Bits(a) == 100, "UDP Int address, type and value");
                Check(Address(b) == "/avatar/parameters/HRPercent" && Type(b) == 'f' && BitConverter.ToSingle(BitConverter.GetBytes(Bits(b)), 0) == 0.5f, "UDP Float address, type and value");
                sender.Send(settings, 300); a = receiver.Receive(ref remote); b = receiver.Receive(ref remote);
                Check(Bits(a) == 255 && BitConverter.ToSingle(BitConverter.GetBytes(Bits(b)), 0) == 1, "VRChat Int and Float range limits");
                settings.OscEnabled = false; sender.Send(settings, 80); Thread.Sleep(50); Check(receiver.Available == 0, "OSC disabled emits no datagrams");
            }

            string database = Path.Combine(folder, "心拍.db");
            using (var store = new Store(database)) {
                store.Add(new Reading { UnixMs = 1000, Bpm = 70, Device = "COOSPO,\"日本語\"", Session = "s1", Contact = true, Rr = "1000;500", Energy = 42 });
                store.Add(new Reading { UnixMs = 2000, Bpm = 90, Device = "HW706", Session = "s1", Contact = null, Rr = "" });
                store.Add(new Reading { UnixMs = 3000, Bpm = 0, Device = "HW706", Session = "s1", Contact = false, Rr = "" });
                store.Add(new Reading { UnixMs = 4000, Bpm = 300, Device = "HW706", Session = "s1", Contact = true, Rr = "" });
                var rows = store.Query(1000, 3000); Check(rows.Count == 2 && rows[0].Bpm == 90 && rows[1].Energy == 42, "half-open history range, descending order, optional fields");
                Check(rows[1].Device == "COOSPO,\"日本語\"" && rows[1].Contact == true && rows[0].Contact == null, "UTF-8 and nullable contact roundtrip");
                var summary = store.Summary(1000, 4000); Check(summary.Count == 2 && summary.Min == 70 && summary.Max == 90 && summary.Average == 80, "statistics exclude zero / absent contact");
                Check(store.Query(0, 5000, 1).Single().Bpm == 300, "raw heart rate is not clamped in storage");
                Check(store.Chart(0, 5000).Count == 3, "history chart excludes invalid readings");
                string csv = Path.Combine(folder, "history.csv"); Check(store.Export(csv, 1000, 3000) == 2, "CSV exports exact selected range");
                Check(File.ReadAllText(csv).Contains("\"COOSPO,\"\"日本語\"\"\""), "CSV quotes delimiters and Unicode");
            }
            using (var reopened = new Store(database)) Check(reopened.Query(0, 5000).Count == 4, "SQLite persists across reopen");
            string boundaryDatabase = Path.Combine(folder, "retention-boundary.db");
            using (var store = new Store(boundaryDatabase)) {
                store.Add(new Reading { UnixMs = 999, Bpm = 70, Device = "test", Session = "retention" });
                store.Add(new Reading { UnixMs = 1000, Bpm = 71, Device = "test", Session = "retention" });
                store.Add(new Reading { UnixMs = 1001, Bpm = 72, Device = "test", Session = "retention" });
                Check(store.DeleteBefore(1000) == 1 && store.Query(0, 2000).Select(row => row.UnixMs).SequenceEqual(new long[] { 1001, 1000 }), "retention removes timestamps strictly before cutoff");
            }
            string startupRetentionDatabase = Path.Combine(folder, "retention-startup.db");
            long retentionNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long recentReading = retentionNow - DayMilliseconds + 10000;
            using (var store = new Store(startupRetentionDatabase)) {
                store.Add(new Reading { UnixMs = retentionNow - DayMilliseconds - 10000, Bpm = 70, Device = "test", Session = "old" });
                store.Add(new Reading { UnixMs = recentReading, Bpm = 72, Device = "test", Session = "recent" });
            }
            settings.OscEnabled = false;
            settings.RetentionDays = 1;
            var retentionEngine = new RecordingEngine(startupRetentionDatabase, settings);
            Check(retentionEngine.StartupRetentionError == null, "startup retention cleanup reports no storage error");
            retentionEngine.StopAsync().GetAwaiter().GetResult();
            using (var store = new Store(startupRetentionDatabase)) Check(store.Query(0, long.MaxValue).Select(row => row.UnixMs).SequenceEqual(new[] { recentReading }), "startup automatically removes readings older than 24 hours");
            string idleRetentionDatabase = Path.Combine(folder, "retention-active-to-idle.db");
            using (var processed = new AutoResetEvent(false))
            using (var deleted = new AutoResetEvent(false)) {
                var idleEngine = new RecordingEngine(idleRetentionDatabase, settings, TimeSpan.FromMilliseconds(200));
                idleEngine.Processed += (row, saved, osc) => processed.Set();
                idleEngine.RetentionChanged += () => deleted.Set();
                try {
                    // Insert after startup cleanup, then leave the queue idle before its deadline.
                    idleEngine.Accept(new Reading { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - DayMilliseconds - 1000, Bpm = 70, Device = "isolated", Session = "idle-retention" });
                    Check(processed.WaitOne(2000), "retention regression sample is processed before idle wait");
                    Check(deleted.WaitOne(2000), "active-to-idle retention wakes at its deadline without another notification");
                    using (var store = new Store(idleRetentionDatabase)) Check(store.Bounds() == null, "scheduled idle retention deletes expired rows from SQLite");
                    idleEngine.Accept(new Reading { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - DayMilliseconds - 1000, Bpm = 71, Device = "isolated", Session = "next-retention" });
                    Check(processed.WaitOne(2000) && deleted.WaitOne(2000), "retention continues on subsequent idle deadlines");
                } finally { idleEngine.StopAsync().GetAwaiter().GetResult(); }
            }
            settings.OscEnabled = false;
            var engine = new RecordingEngine(database, settings);
            var errors = new List<string>(); engine.Error += errors.Add;
            long queuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for (int i = 0; i < 200; i++) engine.Accept(new Reading { UnixMs = queuedAt + i, Bpm = 60 + i % 20, Device = "test", Session = "queue", Rr = "" });
            engine.StopAsync().GetAwaiter().GetResult();
            using (var reopened = new Store(database)) Check(errors.Count == 0 && reopened.Query(queuedAt, queuedAt + 200).Count == 200, "shutdown drains every queued reading");
            Console.WriteLine($"All {count} checks passed. Test artifacts: {folder}"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Artifacts: " + folder); return 1; }
    }

    private static void TestRetentionPolicy(string folder)
    {
        var config = new Settings { OscEnabled = false };
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Check(config.RetentionDays == 0 && config.RetentionCutoff(now) == null, "new settings default to unlimited storage without a deletion cutoff");
        string legacyPath = Path.Combine(folder, "legacy-retention.json");
        File.WriteAllText(legacyPath, "{\"OscEnabled\":false,\"Language\":\"en\"}");
        var legacy = Settings.Load(legacyPath);
        Check(legacy.RetentionDays == 0 && legacy.Language == "en" && !legacy.OscEnabled, "legacy settings migrate to unlimited while preserving other preferences");
        string configPath = Path.Combine(folder, "retention-settings.json");
        foreach (int days in new[] { 0, 1, 7, 30, 90, 365 }) {
            config.RetentionDays = days; config.Save(configPath);
            var loaded = Settings.Load(configPath);
            Check(loaded.RetentionDays == days && loaded.RetentionCutoff(now) == (days == 0 ? (long?)null : now - days * DayMilliseconds), "retention choice persists with correct rolling cutoff: " + days);
        }
        foreach (int days in new[] { -1, 2, int.MaxValue }) {
            var invalid = config.Copy(); invalid.RetentionDays = days;
            Throws(invalid.Validate, "unsupported retention period rejected: " + days);
        }

        config.RetentionDays = 0;
        string database = Path.Combine(folder, "retention-configurable.db");
        long ancient = now - 400 * DayMilliseconds, old = now - 40 * DayMilliseconds, middle = now - 2 * DayMilliseconds, recent = now - 3600000;
        using (var store = new Store(database)) foreach (long time in new[] { ancient, old, middle, recent })
            store.Add(new Reading { UnixMs = time, Bpm = 75, Device = "ISOLATED RETENTION TEST", Session = "retention" });
        using (var deleted = new AutoResetEvent(false))
        using (var processed = new AutoResetEvent(false)) {
            var engine = new RecordingEngine(database, config, TimeSpan.FromMilliseconds(100));
            engine.RetentionChanged += () => deleted.Set();
            engine.Processed += (row, saved, osc) => processed.Set();
            try {
                Check(engine.StartupRetentionError == null && !deleted.WaitOne(300), "unlimited storage performs no startup or scheduled deletion");
                using (var store = new Store(database)) {
                    Check(store.Query(0, long.MaxValue).Count == 4 && store.Bounds().From == ancient, "unlimited keeps recordings older than a year");
                    Check(store.Summary(ancient, ancient + 1).Count == 1 && store.Chart(ancient, recent + 1).Count == 4, "historical statistics and charts include recordings beyond 24 hours");
                    Check(store.Export(Path.Combine(folder, "long-history.csv"), ancient, recent + 1) == 4, "CSV exports old recordings under unlimited retention");
                }
                config.RetentionDays = 30; engine.Configure(config);
                Check(deleted.WaitOne(2000), "saving finite retention applies without restarting or receiving readings");
                using (var store = new Store(database)) Check(store.Query(0, long.MaxValue).Select(r => r.UnixMs).SequenceEqual(new[] { recent, middle }), "thirty-day policy deletes only recordings outside its window");
                config.RetentionDays = 1; engine.Configure(config);
                Check(deleted.WaitOne(2000), "shortening retention triggers another cleanup");
                using (var store = new Store(database)) Check(store.Query(0, long.MaxValue).Single().UnixMs == recent, "shortened retention preserves recent recordings");
                config.RetentionDays = 0; engine.Configure(config);
                engine.Accept(new Reading { UnixMs = ancient, Bpm = 76, Device = "ISOLATED RETENTION TEST", Session = "unlimited-again" });
                Check(processed.WaitOne(2000) && !deleted.WaitOne(300), "returning to unlimited disables subsequent periodic deletion");
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
        }
        var reopenedEngine = new RecordingEngine(database, new Settings { OscEnabled = false });
        reopenedEngine.StopAsync().GetAwaiter().GetResult();
        using (var store = new Store(database)) Check(store.Query(0, long.MaxValue).Count == 2 && store.Bounds().From == ancient, "unlimited restart preserves old data and does not restore previously deleted recordings");
    }
    private static void WaitUntil(Func<bool> condition)
    {
        if (!SpinWait.SpinUntil(condition, 3000)) throw new TimeoutException("Automatic connection test timed out.");
    }
    private static void TestPresentationAndOsc(string folder)
    {
        Check(ConnectionUpdate.Classify(new UnauthorizedAccessException("AccessDenied")) == ConnectionProblem.Permission, "permission failure has actionable category");
        Check(ConnectionUpdate.Classify(new UnauthorizedAccessException("SharingViolation")) == ConnectionProblem.Busy, "sharing violation differs from permission denial");
        Check(ConnectionUpdate.Classify(new Exception("Unreachable")) == ConnectionProblem.Unreachable, "unreachable category");
        Check(ConnectionUpdate.Classify(new OperationCanceledException()) == ConnectionProblem.Timeout, "timeout category");
        Words.Language = "en";
        Check(ConnectionUpdate.Name(ConnectionPhase.AwaitingData).Contains("awaiting") && ConnectionUpdate.Name(ConnectionPhase.Receiving).Contains("Receiving"), "service connection differs from actual samples in English");
        Check(ConnectionUpdate.Advice(ConnectionProblem.Permission).Contains("permissions"), "English recovery advice");
        Words.Language = "ja";
        var start = TimeWindow.At(1000, 10000000, 600000, 0);
        var end = TimeWindow.At(1000, 10000000, 600000, 1);
        Check(start.From == 1000 && start.To == 601000 && end.To == 10000000 && end.From == 9400000, "timeline endpoints retain exact ten-minute scale");
        Check(TimeWindow.At(1000, 10000000, 600000, -2).From == start.From && TimeWindow.At(1000, 10000000, 600000, 2).To == end.To, "timeline clamps drag boundaries");
        var shortRange = TimeWindow.At(0, 100, 600000, 0.5);
        Check(shortRange.To - shortRange.From == 600000, "short recordings keep selected width");
        var config = new Settings { Language = "en", CloseToTray = false };
        var path = Path.Combine(folder, "language.json"); config.Save(path);
        Check(Settings.Load(path).Language == "en" && !Settings.Load(path).CloseToTray, "language and tray preference survive restart");
        config.Language = "xx"; Throws(config.Validate, "unsupported language rejected"); config.Language = "ja";
        var database = Path.Combine(folder, "osc-test-isolation.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            receiver.Client.ReceiveTimeout = 2000;
            config.Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port;
            var engine = new RecordingEngine(database, config);
            OscTransmission sent = null; engine.Transmitted += value => sent = value;
            engine.TestAsync(80).GetAwaiter().GetResult();
            IPEndPoint remote = null;
            var integer = receiver.Receive(ref remote); var percent = receiver.Receive(ref remote);
            Check(Type(integer) == 'i' && Bits(integer) == 80 && Type(percent) == 'f' && Bits(percent) == BitConverter.ToInt32(BitConverter.GetBytes(0.4f), 0), "one-shot test sends configured Int and Float datagrams");
            Check(sent != null && sent.Test && sent.Integer == 80 && sent.Percent == 0.4f && sent.Endpoint.EndsWith(":" + config.Port), "OSC status reports actual test payload and endpoint");
            using (var store = new Store(database)) Check(store.Bounds() == null && store.Query(0, long.MaxValue).Count == 0, "OSC test never creates a stored measurement");
            config.OscEnabled = false; engine.Configure(config);
            Throws(() => engine.TestAsync(90).GetAwaiter().GetResult(), "OSC test respects disabled output");
            Check(receiver.Available == 0, "disabled OSC test sends no datagram");
            engine.Accept(new Reading { UnixMs = 1000, Bpm = 75, Device = "isolated test", Session = "test" });
            engine.Accept(new Reading { UnixMs = 2000, Bpm = 76, Device = "isolated test", Session = "test" });
            engine.StopAsync().GetAwaiter().GetResult();
            using (var store = new Store(database)) { var bounds = store.Bounds(); Check(bounds.From == 1000 && bounds.To == 2001, "history bounds include final sample in half-open window"); }
        }
    }
    private static void TestRecordingPipeline(string folder)
    {
        string database = Path.Combine(folder, "recording-pipeline.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            receiver.Client.ReceiveTimeout = 2000;
            var config = new Settings { Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port };
            var results = new List<string>();
            var transmissions = new List<OscTransmission>();
            var engine = new RecordingEngine(database, config);
            engine.Processed += (reading, storage, sending) => results.Add(storage + ":" + sending);
            engine.Transmitted += transmissions.Add;
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 80, Contact = null });
                engine.Accept(new Reading { UnixMs = 2000, Bpm = 90, Contact = false });
                engine.Accept(new Reading { UnixMs = 3000, Bpm = 0, Contact = true });
                engine.Accept(new Reading { UnixMs = 4000, Bpm = 300, Contact = true });
            } finally { engine.StopAsync().GetAwaiter().GetResult(); }
            Check(results.SequenceEqual(new[] { "saved:sent", "saved:waiting", "saved:waiting", "saved:sent" }), "recording pipeline saves every notification and suppresses invalid OSC samples in queue order");
            Check(transmissions.Count == 2 && transmissions.All(t => !t.Test) && transmissions[0].Integer == 80 && transmissions[1].Integer == 255 && transmissions[1].Percent == 1, "live OSC events identify real transmissions and report clamped payloads");
            IPEndPoint remote = null;
            Check(Bits(receiver.Receive(ref remote)) == 80, "live unknown-contact sample sends its actual integer value");
            receiver.Receive(ref remote);
            Check(Bits(receiver.Receive(ref remote)) == 255, "live high-rate sample clamps only its OSC integer");
            receiver.Receive(ref remote);
            Check(receiver.Available == 0, "zero and absent-contact samples emit no extra OSC datagrams");
            using (var store = new Store(database))
                Check(store.Query(0, long.MaxValue).Select(r => r.Bpm).SequenceEqual(new[] { 300, 0, 90, 80 }), "recording pipeline preserves raw readings even when OSC suppresses or clamps them");
        }

        string snapshotDatabase = Path.Combine(folder, "recording-config-snapshot.db");
        using (var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var entered = new ManualResetEventSlim())
        using (var resume = new ManualResetEventSlim()) {
            receiver.Client.ReceiveTimeout = 2000;
            var config = new Settings { Port = ((IPEndPoint)receiver.Client.LocalEndPoint).Port };
            var engine = new RecordingEngine(snapshotDatabase, config);
            var results = new List<string>();
            int failures = 0;
            engine.OscFailed += error => failures++;
            engine.Processed += (reading, storage, sending) => {
                results.Add(storage + ":" + sending);
                if (reading.UnixMs == 1000) { entered.Set(); resume.Wait(); }
            };
            try {
                engine.Accept(new Reading { UnixMs = 1000, Bpm = 0 });
                Check(entered.Wait(3000), "isolated worker barrier is reached before queuing a test");
                var pendingTest = engine.TestAsync(80);
                config.OscEnabled = false;
                engine.Configure(config);
                resume.Set();
                Check(pendingTest.Wait(3000), "queued OSC test retains the configuration captured at request time");
                IPEndPoint remote = null;
                Check(Bits(receiver.Receive(ref remote)) == 80, "queued OSC test sends using its enabled configuration snapshot");
                receiver.Receive(ref remote);
                Throws(() => engine.TestAsync(90).GetAwaiter().GetResult(), "later OSC test uses the newly disabled configuration");
                engine.Accept(new Reading { UnixMs = 2000, Bpm = 75 });
            } finally {
                resume.Set();
                engine.StopAsync().GetAwaiter().GetResult();
            }
            Check(failures == 1 && results.SequenceEqual(new[] { "saved:waiting", "saved:disabled" }), "OSC test failure reports once and the worker continues saving with current settings");
            Check(receiver.Available == 0, "disabled live output and failed test emit no datagrams");
            using (var store = new Store(snapshotDatabase))
                Check(store.Query(0, long.MaxValue).Select(r => r.Bpm).SequenceEqual(new[] { 75, 0 }), "queued tests never persist readings even across configuration changes");
        }
    }
    private static void TestGattAccess()
    {
        foreach (var denied in new[] { DeviceAccessStatus.DeniedBySystem, DeviceAccessStatus.DeniedByUser, DeviceAccessStatus.Unspecified }) {
            int openCalls = 0; bool rejected = false;
            try {
                GattAccess.EnsureAsync(() => Task.FromResult(denied), () => { openCalls++; return Task.FromResult(GattOpenStatus.Success); }, null, CancellationToken.None).GetAwaiter().GetResult();
            } catch (UnauthorizedAccessException) { rejected = true; }
            Check(rejected && openCalls == 0, "GATT denied permission never opens service: " + denied);
        }
        foreach (var opened in new[] { GattOpenStatus.Success, GattOpenStatus.AlreadyOpened }) {
            var steps = new List<string>();
            GattAccess.EnsureAsync(() => { steps.Add("permission"); return Task.FromResult(DeviceAccessStatus.Allowed); }, () => { steps.Add("open"); return Task.FromResult(opened); }, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(steps.SequenceEqual(new[] { "permission", "open" }), "GATT permission precedes usable shared service: " + opened);
        }
        bool sharingRejected = false;
        try { GattAccess.EnsureAsync(() => Task.FromResult(DeviceAccessStatus.Allowed), () => Task.FromResult(GattOpenStatus.SharingViolation), null, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (UnauthorizedAccessException ex) { sharingRejected = ex.Message.Contains("SharingViolation"); }
        Check(sharingRejected, "exclusive owner is reported instead of accessing blocked service");
    }
    private static void Stop(AutoConnector auto)
    {
        if (!auto.StopAsync().Wait(3000)) throw new TimeoutException("Stop did not cancel the active operation.");
    }
    private static void TestAutoConnection()
    {
        var hw = new DeviceChoice { Address = 10, Name = "COOSPO HW706", HasHeartRateService = true };
        var heart = new DeviceChoice { Address = 20, Name = "Other sensor", HasHeartRateService = true };
        var speaker = new DeviceChoice { Address = 30, Name = "Speaker" };
        Check(AutoConnector.Candidates(new[] { speaker, heart, hw }, 0).Select(d => d.Address).SequenceEqual(new ulong[] { 10, 20 }), "auto discovery selects standard heart services and excludes unrelated devices");
        Check(AutoConnector.Candidates(new[] { hw, heart }, 20).First().Address == 20, "previously connected sensor has highest priority");
        Check(AutoConnector.Candidates(new[] { speaker }, 30).Count == 0, "legacy saved non-heart device is never auto-connected");
        Check(AutoConnector.Candidates(new[] { new DeviceChoice { Name = "Windows heart sensor", ServiceId = "registered-service", HasHeartRateService = true } }, 0).Count == 1, "registered heart service without advertisement address is discoverable");
        Check(AutoConnector.Candidates(new[] { new DeviceChoice { Address = 1, Name = "COOSPO HW706" } }, 0).Count == 0, "model name alone never qualifies an unverified device");
        Check(AutoConnector.Candidates(new[] { hw, new DeviceChoice { Address = 5, Name = "Any brand", HasHeartRateService = true } }, 0).First().Address == 5, "automatic selection does not favor a manufacturer or model");
        var config = new Settings();
        Func<TimeSpan, CancellationToken, Task> quickDelay = (span, token) => Task.Delay(5, token);

        var fake = new FakeDiscovery();
        fake.Search = (n, token) => Task.FromResult(n == 1 ? new List<DeviceChoice>() : new List<DeviceChoice> { speaker, hw });
        var auto = new AutoConnector(fake, quickDelay); auto.Start(() => config); auto.Start(() => config);
        WaitUntil(() => fake.ConnectCalls == 1);
        Check(fake.ScanCalls == 2 && fake.LastAddress == 10, "empty first scan retries and connects when sensor appears");
        Stop(auto); int scans = fake.ScanCalls; Thread.Sleep(20);
        Check(!auto.Active && fake.ScanCalls == scans && fake.ConnectCalls == 1, "stop cancels connection and prevents background restart");

        fake = new FakeDiscovery(); fake.Search = async (n, token) => { await Task.Delay(Timeout.Infinite, token); return new List<DeviceChoice>(); };
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => config); Stop(auto);
        Check(fake.ScanCalls == 1 && fake.ConnectCalls == 0 && !auto.Active, "stop during discovery cancels without connecting");

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice>());
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => new Settings { LastAddress = 30, LastName = "Quest 3S" }); WaitUntil(() => fake.ScanCalls >= 2); Stop(auto);
        Check(fake.ConnectCalls == 0, "unverified legacy address is not used as fallback");

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice>());
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => new Settings { LastAddress = 10, LastName = "HW706", LastDeviceVerified = true }); WaitUntil(() => fake.ConnectCalls == 1); Stop(auto);
        Check(fake.LastAddress == 10, "verified sensor can reconnect without advertising");

        var previousConfig = new Settings { LastAddress = 99, LastName = "Any previous sensor", LastDeviceId = "saved-device", LastDeviceVerified = true };
        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { hw, heart });
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => previousConfig); WaitUntil(() => fake.ConnectCalls == 1);
        Check(fake.LastAddress == 99 && fake.LastDeviceId == "saved-device", "non-advertising verified sensor precedes other discovered sensors"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { heart });
        fake.Connect = (d, n, token) => n == 1 ? Task.FromException(new Exception("Previous sensor unavailable")) : Task.Delay(Timeout.Infinite, token);
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => previousConfig); WaitUntil(() => fake.ConnectCalls == 2);
        Check(fake.Attempts.SequenceEqual(new ulong[] { 99, 20 }), "unavailable previous sensor falls through to a discovered heart service"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { new DeviceChoice { Address = 99, DeviceId = "current-device", Name = "Renamed sensor" }, heart });
        fake.Connect = (d, n, token) => Task.FromException(new UnauthorizedAccessException("DeniedByUser"));
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => previousConfig); WaitUntil(() => !auto.Active);
        Check(fake.ConnectCalls == 1 && fake.LastDeviceId == "current-device", "verified sensor uses current discovery identity even without service advertisement"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { heart });
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => new Settings { LastDeviceVerified = true, LastDeviceId = "id-only", LastName = "ID-only sensor" }); WaitUntil(() => fake.ConnectCalls == 1);
        Check(fake.LastDeviceId == "id-only", "verified device identity can be retried without a saved address"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { hw, heart });
        fake.Connect = (d, n, token) => n == 1 ? Task.FromException(new Exception("GATT unavailable")) : Task.Delay(Timeout.Infinite, token);
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => config); WaitUntil(() => fake.ConnectCalls == 2);
        Check(fake.LastAddress == 20, "failed candidate falls through to next sensor"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => n == 1 ? Task.FromException<List<DeviceChoice>>(new Exception("Radio off")) : Task.FromResult(new List<DeviceChoice> { hw });
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => config); WaitUntil(() => fake.ConnectCalls == 1);
        Check(fake.ScanCalls == 2, "radio failure retries after recovery"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { hw });
        var connectedFake = fake;
        fake.Connect = (d, n, token) => { connectedFake.ReportConnected(d); return n == 1 ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token); };
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => config); WaitUntil(() => fake.ConnectCalls == 2);
        Check(fake.ScanCalls == 2 && fake.LastAddress == 10, "disconnect re-discovers and reconnects the same sensor"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { hw });
        connectedFake = fake;
        fake.Connect = (d, n, token) => { connectedFake.ReportConnected(d); return Task.CompletedTask; };
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => new Settings { AutoReconnect = false }); WaitUntil(() => !auto.Active);
        Check(fake.ScanCalls == 1 && fake.ConnectCalls == 1, "auto-reconnect off stops after disconnect"); Stop(auto);

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { hw });
        fake.Connect = (d, n, token) => Task.FromException(new UnauthorizedAccessException("DeniedByUser"));
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => config); WaitUntil(() => !auto.Active);
        Thread.Sleep(20);
        Check(fake.ScanCalls == 1 && fake.ConnectCalls == 1, "access denied stops auto retry instead of repeating permission requests"); Stop(auto);
    }
    private sealed class FakeDiscovery : IHeartRateDiscovery
    {
        public event Action<DeviceChoice> Connected;
        public Func<int, CancellationToken, Task<List<DeviceChoice>>> Search;
        public Func<DeviceChoice, int, CancellationToken, Task> Connect;
        public int ScanCalls, ConnectCalls;
        public ulong LastAddress;
        public string LastDeviceId;
        public readonly List<ulong> Attempts = new List<ulong>();
        public void ReportConnected(DeviceChoice choice) => Connected?.Invoke(choice);
        public Task<List<DeviceChoice>> ScanAsync(CancellationToken token) => Search(Interlocked.Increment(ref ScanCalls), token);
        public Task ConnectOnceAsync(DeviceChoice choice, CancellationToken token)
        {
            LastAddress = choice.Address; LastDeviceId = choice.DeviceId; Attempts.Add(choice.Address); int n = Interlocked.Increment(ref ConnectCalls);
            return Connect == null ? Task.Delay(Timeout.Infinite, token) : Connect(choice, n, token);
        }
    }
}
