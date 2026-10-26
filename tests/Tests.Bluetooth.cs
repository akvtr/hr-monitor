using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeartRateMonitor;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

internal static partial class Tests
{
    private static void TestReceptionTimer()
    {
        long timestamp = 100 * Stopwatch.Frequency;
        var timer = new ReceptionTimer(() => Interlocked.Read(ref timestamp));
        Check(timer.ElapsedSeconds == 0, "reception interval starts at construction even with a nonzero clock origin");
        Interlocked.Add(ref timestamp, 19 * Stopwatch.Frequency);
        Check(timer.ElapsedSeconds == 19, "reception silence advances using monotonic ticks rather than calendar time");
        Interlocked.Add(ref timestamp, Stopwatch.Frequency);
        Check(timer.ElapsedSeconds == 20, "reception timer preserves the exact Bluetooth timeout boundary");
        timer.Reset();
        Check(timer.ElapsedSeconds == 0, "new reception restarts the silence interval");
        Interlocked.Add(ref timestamp, 5 * Stopwatch.Frequency);
        Check(timer.ElapsedSeconds == 5, "reception timer supports the OSC silence threshold after a reset");
    }

    private static void TestMeasurements()
    {
        var m = Measurement.Parse(new byte[] { 0, 72 }); Check(m.Bpm == 72 && m.Contact == null, "8-bit heart rate and unsupported contact");
        m = Measurement.Parse(new byte[] { 1, 44, 1 }); Check(m.Bpm == 300, "16-bit little-endian heart rate");
        m = Measurement.Parse(new byte[] { 30, 80, 42, 0, 0, 4, 0, 2 });
        Check(m.Bpm == 80 && m.Contact == true && m.Energy == 42 && m.RrMilliseconds.SequenceEqual(new[] { 1000.0, 500.0 }), "contact, energy and multiple RR intervals");
        Check(Measurement.Parse(new byte[] { 4, 0 }).Contact == false, "absent contact");
        Throws<FormatException>(() => Measurement.Parse(new byte[] { 1, 3 }), "truncated 16-bit measurement");
        Throws<FormatException>(() => Measurement.Parse(new byte[] { 16, 80, 1 }), "truncated RR interval");
        Throws<FormatException>(() => Measurement.Parse(new byte[] { 16, 80 }), "missing RR interval");
        Throws<FormatException>(() => Measurement.Parse(new byte[] { 8, 80, 1 }), "truncated energy");
        Throws<FormatException>(() => Measurement.Parse(new byte[] { 0, 80, 1 }), "unexpected trailing bytes");
        Throws<FormatException>(() => Measurement.Parse(null), "null measurement");
    }

    private static void TestMeasurementFlagCombinations()
    {
        for (int flags = 0; flags < 32; flags++) {
            var packet = new List<byte> { (byte)flags, 255 };
            if ((flags & 1) != 0) packet.Add(255);
            if ((flags & 8) != 0) packet.AddRange(new byte[] { 255, 255 });
            if ((flags & 16) != 0) packet.AddRange(new byte[] { 0, 0, 1, 0, 0, 4, 255, 255 });
            byte[] bytes = packet.ToArray(), original = (byte[])bytes.Clone();
            var measurement = Measurement.Parse(bytes);
            bool? contact = (flags & 4) == 0 ? (bool?)null : (flags & 2) != 0;
            double[] rr = (flags & 16) == 0 ? new double[0] : new[] { 0, 1000.0 / 1024, 1000, 65535 * 1000.0 / 1024 };
            Check(measurement.Bpm == ((flags & 1) == 0 ? 255 : 65535) && measurement.Contact == contact &&
                measurement.Energy == ((flags & 8) == 0 ? (int?)null : 65535) && measurement.RrMilliseconds.SequenceEqual(rr) && bytes.SequenceEqual(original),
                "measurement parser preserves every field and input bytes for flags " + flags);
        }
        byte[] complete = { 31, 255, 255, 255, 255, 0, 0, 1, 0, 0, 4, 255, 255 };
        foreach (int length in new[] { 0, 1, 2, 3, 4, 5, 6, 8, 10, 12 })
            Throws<FormatException>(() => Measurement.Parse(complete.Take(length).ToArray()), "measurement parser rejects a truncated required field at byte length " + length);
        foreach (int length in new[] { 7, 9, 11 })
            Check(Measurement.Parse(complete.Take(length).ToArray()).RrMilliseconds.Length == (length - 5) / 2,
                "measurement parser accepts each complete variable RR prefix at byte length " + length);
    }

    private static void TestConnectionInputValidation()
    {
        Throws<ArgumentNullException>(() => new AutoConnector(null), "automatic connection rejects a missing transport before subscribing to events");
        var fake = new FakeDiscovery { Search = async (n, token) => { await Task.Delay(Timeout.Infinite, token); return new List<DeviceChoice>(); } };
        var auto = new AutoConnector(fake);
        try {
            Throws<ArgumentNullException>(() => auto.Start(null), "automatic connection rejects a missing settings provider before starting discovery");
            Check(fake.ScanCalls == 0 && !auto.Active, "invalid automatic connection inputs cause no radio activity");
        } finally { Stop(auto); }
        var bluetooth = new BluetoothMonitor();
        int notifications = 0;
        bluetooth.Status += message => notifications++;
        try {
            Throws<ArgumentNullException>(() => bluetooth.Start(null, () => false), "manual connection rejects a missing device synchronously");
            Throws<ArgumentNullException>(() => bluetooth.Start(new DeviceChoice { Address = 1 }, null), "manual connection rejects a missing reconnect callback synchronously");
            Check(!bluetooth.Active && notifications == 0, "invalid manual connection inputs leave the monitor idle without connection notifications");
        } finally { bluetooth.StopAsync().GetAwaiter().GetResult(); }
        int requests = 0, opens = 0;
        Func<Task<DeviceAccessStatus>> request = () => { requests++; return Task.FromResult(DeviceAccessStatus.Allowed); };
        Func<Task<GattOpenStatus>> open = () => { opens++; return Task.FromResult(GattOpenStatus.Success); };
        Throws<ArgumentNullException>(() => GattAccess.EnsureAsync(null, open, null, CancellationToken.None).GetAwaiter().GetResult(), "GATT rejects a missing permission callback explicitly");
        Throws<ArgumentNullException>(() => GattAccess.EnsureAsync(request, null, null, CancellationToken.None).GetAwaiter().GetResult(), "GATT rejects a missing open callback before requesting permission");
        Check(requests == 0 && opens == 0, "invalid GATT callbacks never request device access or open a service");
    }

    private static void TestGattCancellation()
    {
        using (var canceled = new CancellationTokenSource()) {
            canceled.Cancel();
            int calls = 0;
            Task attempt = GattAccess.EnsureAsync(() => { calls++; return Task.FromResult(DeviceAccessStatus.Allowed); }, () => { calls++; return Task.FromResult(GattOpenStatus.Success); }, null, canceled.Token);
            Throws<OperationCanceledException>(() => attempt.GetAwaiter().GetResult(), "pre-canceled GATT access completes as cancellation");
            Check(attempt.IsCanceled && calls == 0, "pre-canceled GATT access never invokes permission or open callbacks");
        }
        foreach (var permission in new[] { DeviceAccessStatus.Allowed, DeviceAccessStatus.DeniedByUser }) {
            using (var canceled = new CancellationTokenSource()) {
                var response = new TaskCompletionSource<DeviceAccessStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                int opens = 0;
                Task attempt = GattAccess.EnsureAsync(() => response.Task, () => { opens++; return Task.FromResult(GattOpenStatus.Success); }, null, canceled.Token);
                canceled.Cancel(); response.SetResult(permission);
                Throws<OperationCanceledException>(() => attempt.GetAwaiter().GetResult(), "canceled permission request stays canceled when Windows completes with " + permission);
                Check(attempt.IsCanceled && opens == 0, "permission completion after cancellation never opens the GATT service: " + permission);
            }
        }
        using (var canceled = new CancellationTokenSource()) {
            var response = new TaskCompletionSource<GattOpenStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task attempt = GattAccess.EnsureAsync(() => Task.FromResult(DeviceAccessStatus.Allowed), () => response.Task, null, canceled.Token);
            canceled.Cancel(); response.SetResult(GattOpenStatus.AccessDenied);
            Throws<OperationCanceledException>(() => attempt.GetAwaiter().GetResult(), "canceled service open is not misreported as a permission failure");
            Check(attempt.IsCanceled, "canceled GATT service open returns a canceled task rather than a faulted task");
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
        foreach (GattCommunicationStatus status in Enum.GetValues(typeof(GattCommunicationStatus))) {
            string detail = "isolated GATT stage: " + status + ", ATT=5";
            Exception failure = null;
            try { GattAccess.EnsureCommunication(status, detail); }
            catch (Exception ex) { failure = ex; }
            if (status == GattCommunicationStatus.Success)
                Check(failure == null, "successful GATT communication continues");
            else if (status == GattCommunicationStatus.AccessDenied)
                Check(failure is UnauthorizedAccessException && failure.Message.Contains(detail) && ConnectionUpdate.Classify(failure) == ConnectionProblem.Permission,
                    "GATT communication access denial preserves stage details and requires permission recovery");
            else
                Check(failure is InvalidOperationException && failure.Message == detail,
                    "transient GATT communication failure preserves retryable details: " + status);
        }
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
        var firstService = new DeviceChoice { DeviceId = "", ServiceId = "first-service", HasHeartRateService = true };
        var secondService = new DeviceChoice { DeviceId = "", ServiceId = "second-service", HasHeartRateService = true };
        var candidates = AutoConnector.Candidates(new[] { firstService, secondService, firstService, hw, hw }, 0);
        Check(candidates.Count == 3 && candidates.Contains(firstService) && candidates.Contains(secondService),
            "candidate deduplication retains distinct registered services with empty device IDs");
        Check(AutoConnector.Candidates(new[] {
            new DeviceChoice { DeviceId = "same-id", HasHeartRateService = true },
            new DeviceChoice { ServiceId = "same-id", HasHeartRateService = true }
        }, 0).Count == 2, "candidate identity distinguishes device and service IDs");
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

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { firstService, secondService });
        string connectedService = null;
        fake.Connect = (d, n, token) => {
            Interlocked.Exchange(ref connectedService, d.ServiceId);
            return n == 1 ? Task.FromException(new InvalidOperationException("Service unavailable")) : Task.Delay(Timeout.Infinite, token);
        };
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => config);
        try { WaitUntil(() => Volatile.Read(ref connectedService) == "second-service"); }
        finally { Stop(auto); }
        Check(fake.ScanCalls == 1 && fake.ConnectCalls == 2 && connectedService == "second-service",
            "unavailable registered service falls through to a distinct service with an empty device ID");

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

        fake = new FakeDiscovery(); fake.Search = (n, token) => Task.FromResult(new List<DeviceChoice> { hw, heart });
        fake.Connect = (d, n, token) => {
            GattAccess.EnsureCommunication(GattCommunicationStatus.AccessDenied, "GATT notification configuration: AccessDenied");
            return Task.CompletedTask;
        };
        auto = new AutoConnector(fake, quickDelay);
        ConnectionUpdate deniedState = null;
        auto.StateChanged += state => deniedState = state;
        auto.Start(() => config); WaitUntil(() => !auto.Active); Stop(auto);
        Check(fake.ScanCalls == 1 && fake.ConnectCalls == 1 && deniedState.Phase == ConnectionPhase.Error && deniedState.Problem == ConnectionProblem.Permission,
            "GATT communication access denial stops candidate fallback and reports permission failure");
    }

    private static void TestDiscoveryFailureFallback()
    {
        var saved = new Settings { LastAddress = 99, LastDeviceId = "verified-device", LastName = "Verified sensor", LastDeviceVerified = true };
        Func<TimeSpan, CancellationToken, Task> quickDelay = (span, token) => Task.Delay(5, token);
        var heart = new DeviceChoice { Address = 20, HasHeartRateService = true, Name = "Heart sensor" };
        var fake = new FakeDiscovery { Search = (n, token) => Task.FromException<List<DeviceChoice>>(new InvalidOperationException("Discovery unavailable")) };
        var auto = new AutoConnector(fake, quickDelay);
        auto.Start(() => saved);
        try {
            WaitUntil(() => fake.ConnectCalls > 0);
            Check(fake.ScanCalls == 1 && fake.LastAddress == 99 && fake.LastDeviceId == "verified-device",
                "failed discovery still attempts the previously verified sensor directly");
        } finally { Stop(auto); }

        fake = new FakeDiscovery { Search = (n, token) => n == 1 ? Task.FromException<List<DeviceChoice>>(new InvalidOperationException("Discovery unavailable")) : Task.FromResult(new List<DeviceChoice> { heart }) };
        auto = new AutoConnector(fake, quickDelay);
        auto.Start(() => new Settings { LastAddress = 99, LastName = "Unverified device" });
        try {
            WaitUntil(() => fake.ConnectCalls > 0);
            Check(fake.ScanCalls == 2 && fake.LastAddress == 20, "discovery failure never qualifies an unverified saved address");
        } finally { Stop(auto); }

        fake = new FakeDiscovery { Search = (n, token) => Task.FromException<List<DeviceChoice>>(new UnauthorizedAccessException("DeniedByUser")) };
        auto = new AutoConnector(fake, quickDelay);
        ConnectionUpdate state = null;
        auto.StateChanged += update => state = update;
        auto.Start(() => saved); WaitUntil(() => !auto.Active); Stop(auto);
        Check(fake.ScanCalls == 1 && fake.ConnectCalls == 0 && state.Phase == ConnectionPhase.Error && state.Problem == ConnectionProblem.Permission,
            "discovery permission denial stops without a saved-device fallback");

        fake = new FakeDiscovery { Search = async (n, token) => { await Task.Delay(Timeout.Infinite, token); return new List<DeviceChoice>(); } };
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => saved); Stop(auto);
        Check(fake.ConnectCalls == 0 && !auto.Active, "canceled discovery never starts a saved-device fallback");

        fake = new FakeDiscovery { Search = (n, token) => n == 1 ? Task.FromResult(new List<DeviceChoice> { heart }) : Task.FromException<List<DeviceChoice>>(new InvalidOperationException("Discovery unavailable")) };
        var transport = fake;
        fake.Connect = (choice, n, token) => {
            transport.ReportConnected(choice);
            return n == 1 ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);
        };
        auto = new AutoConnector(fake, quickDelay); auto.Start(() => new Settings());
        try {
            WaitUntil(() => fake.ConnectCalls >= 2);
            Check(fake.ScanCalls == 2 && fake.Attempts.SequenceEqual(new ulong[] { 20, 20 }),
                "discovery failure after disconnect retries the same verified sensor");
        } finally { Stop(auto); }
    }

    private static void TestBluetoothRetryClassification()
    {
        var monitor = new BluetoothMonitor();
        var failure = new TimeoutException("ISOLATED RETRY TIMEOUT");
        bool started = false;
        ConnectionUpdate error = null, retry = null;
        Task stop = null;
        monitor.Status += message => {
            if (started) return;
            started = true;
            // Fail before opening a real device; retries must retain this exception's category.
            throw failure;
        };
        monitor.StateChanged += state => {
            if (state.Phase == ConnectionPhase.Error) error = state;
            if (state.Phase == ConnectionPhase.Retrying) { retry = state; stop = monitor.StopAsync(); }
        };
        try {
            monitor.Start(new DeviceChoice { Address = 1, Name = "ISOLATED RETRY TEST" }, () => true);
            Check(error != null && error.Problem == ConnectionProblem.Timeout, "manual connection reports a typed timeout without relying on message keywords");
            Check(retry != null && retry.Problem == error.Problem && retry.Detail.Contains(failure.Message),
                "retry status preserves the original failure category and diagnostic detail");
            Check(stop != null && stop.Wait(3000) && !monitor.Active, "manual retry remains cancellable before another device open");
        } finally { monitor.StopAsync().GetAwaiter().GetResult(); }
    }

    private static void TestBluetoothConnectionReentrancy()
    {
        var monitor = new BluetoothMonitor();
        var choice = new DeviceChoice { Address = 1, Name = "ISOLATED REENTRANCY TEST" };
        bool notified = false, activeDuringNotification = false, rejectedRestart = false;
        monitor.Status += message => {
            if (notified) return;
            notified = true;
            activeDuringNotification = monitor.Active;
            // Avoid starting any nested operation on the unfixed implementation.
            if (activeDuringNotification) {
                try { monitor.Start(choice, () => false); }
                catch (InvalidOperationException) { rejectedRestart = true; }
            }
            // Abort before any WinRT open/permission calls, keeping this test hardware-free.
            throw new UnauthorizedAccessException("ISOLATED EARLY STOP");
        };
        try {
            monitor.Start(choice, () => false);
            Check(activeDuringNotification, "manual connection is active before its first synchronous status notification");
            Check(rejectedRestart, "starting manual connection from a notification rejects an overlapping operation");
        } finally { monitor.StopAsync().GetAwaiter().GetResult(); }
        Check(!monitor.Active, "manual connection releases its current operation after an early failure");
    }

    private static void TestAutomaticConnectionReentrancy()
    {
        var scan = new TaskCompletionSource<List<DeviceChoice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeDiscovery { Search = (number, token) => scan.Task };
        var auto = new AutoConnector(fake);
        bool notified = false, activeDuringNotification = false;
        auto.Status += message => {
            if (notified) return;
            notified = true;
            activeDuringNotification = auto.Active;
            auto.Start(() => new Settings());
        };
        try {
            auto.Start(() => new Settings());
            Check(activeDuringNotification, "automatic connection is active before its first synchronous status notification");
            Check(fake.ScanCalls == 1, "starting again from a status notification never creates a second discovery loop");
        } finally {
            scan.TrySetException(new UnauthorizedAccessException("ISOLATED REENTRANCY TEST"));
            Stop(auto);
            WaitUntil(() => fake.ConnectedSubscribers == 0);
        }

        scan = new TaskCompletionSource<List<DeviceChoice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake = new FakeDiscovery { Search = (number, token) => scan.Task };
        auto = new AutoConnector(fake);
        Task stop = null;
        auto.Status += message => { if (stop == null) stop = auto.StopAsync(); };
        try {
            auto.Start(() => new Settings());
            Check(stop != null && !stop.IsCompleted && fake.ScanCalls == 1,
                "stopping from the initial notification waits for discovery cleanup instead of an old completed task");
            scan.SetResult(new List<DeviceChoice>());
            Check(stop.Wait(3000) && !auto.Active && fake.ConnectedSubscribers == 0,
                "reentrant stop completes only after the current discovery loop releases its subscription");
        } finally { scan.TrySetResult(new List<DeviceChoice>()); Stop(auto); }
    }

    private sealed class FakeDiscovery : IHeartRateDiscovery
    {
        public event Action<DeviceChoice> Connected;
        public int ConnectedSubscribers => Connected?.GetInvocationList().Length ?? 0;
        public Func<int, CancellationToken, Task<List<DeviceChoice>>> Search;
        public Func<DeviceChoice, int, CancellationToken, Task> Connect;
        private int scanCalls, connectCalls;
        public int ScanCalls => Volatile.Read(ref scanCalls);
        public int ConnectCalls => Volatile.Read(ref connectCalls);
        public ulong LastAddress;
        public string LastDeviceId;
        public readonly List<ulong> Attempts = new List<ulong>();
        public void ReportConnected(DeviceChoice choice) => Connected?.Invoke(choice);
        public Task<List<DeviceChoice>> ScanAsync(CancellationToken token) => Search(Interlocked.Increment(ref scanCalls), token);
        public Task ConnectOnceAsync(DeviceChoice choice, CancellationToken token)
        {
            LastAddress = choice.Address; LastDeviceId = choice.DeviceId; Attempts.Add(choice.Address); int n = Interlocked.Increment(ref connectCalls);
            return Connect == null ? Task.Delay(Timeout.Infinite, token) : Connect(choice, n, token);
        }
    }
}
