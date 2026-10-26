using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeartRateMonitor
{
    internal sealed class OyasumiSleepStatus
    {
        internal readonly string State, Detail;
        internal OyasumiSleepStatus(string state, string detail = "") { State = state; Detail = detail; }
    }
    internal sealed class OyasumiSleepMonitor
    {
        private readonly object gate = new object();
        private readonly string database;
        private readonly Func<List<int>> discover;
        private readonly Func<int, CancellationToken, bool> read;
        private readonly TimeSpan interval;
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private CancellationTokenSource request;
        private Settings configuration;
        private Task worker = Task.CompletedTask;
        private bool started;
        private int revision;
        private volatile OyasumiSleepStatus status = new OyasumiSleepStatus("Waiting");
        internal OyasumiSleepStatus Status => status;
        internal event Action<OyasumiSleepStatus> StatusChanged;
        internal event Action DataChanged;
        internal OyasumiSleepMonitor(string database, Settings settings, Func<List<int>> discover = null,
            Func<int, CancellationToken, bool> read = null, TimeSpan? interval = null)
        {
            settings.Validate();
            this.database = database; configuration = settings.Copy();
            this.discover = discover ?? OyasumiPorts.Find; this.read = read ?? new OyasumiOscQuery().Read;
            this.interval = interval ?? TimeSpan.FromSeconds(2);
            if (this.interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            if (!settings.OyasumiEnabled) status = new OyasumiSleepStatus("Disabled");
        }
        internal void Configure(Settings settings)
        {
            settings.Validate();
            lock (gate) {
                if (configuration.OyasumiEnabled == settings.OyasumiEnabled && configuration.OyasumiQueryPort == settings.OyasumiQueryPort) return;
                configuration = settings.Copy(); revision++; request?.Cancel();
            }
        }
        internal void Start()
        {
            lock (gate) { if (started || stopping.IsCancellationRequested) return; started = true; worker = Task.Run((Func<Task>)Run); }
        }
        internal Task StopAsync()
        {
            lock (gate) { stopping.Cancel(); request?.Cancel(); return worker; }
        }
        private async Task Run()
        {
            int generation = -1, cachedPort = 0;
            SleepIntervalTracker tracker = null;
            try {
                while (!stopping.IsCancellationRequested) {
                    Settings current; int currentRevision;
                    lock (gate) {
                        current = configuration; currentRevision = revision;
                        request = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    }
                    try {
                        if (generation != currentRevision) { tracker = null; cachedPort = 0; generation = currentRevision; }
                        if (!current.OyasumiEnabled) Publish("Disabled");
                        else {
                            bool? asleep = null; Exception failure = null;
                            var ports = current.OyasumiQueryPort != 0 ? new List<int> { current.OyasumiQueryPort }
                                : cachedPort != 0 ? new List<int> { cachedPort } : discover();
                            foreach (int port in ports) {
                                try { asleep = read(port, request.Token); cachedPort = port; break; }
                                catch (Exception ex) { if (request.IsCancellationRequested) throw new OperationCanceledException(); failure = ex; }
                            }
                            lock (gate) {
                                if (currentRevision != revision || stopping.IsCancellationRequested) continue;
                                if (!asleep.HasValue) {
                                    tracker = null; cachedPort = 0;
                                    Publish(failure == null ? "Waiting" : "Unavailable", failure?.Message ?? "");
                                } else {
                                    Observe(asleep.Value, ref tracker);
                                    Publish(asleep.Value ? "Sleeping" : "Awake", "127.0.0.1:" + cachedPort);
                                }
                            }
                        }
                    } catch (OperationCanceledException) { tracker = null; }
                    catch (Exception ex) {
                        tracker = null; cachedPort = 0;
                        lock (gate) {
                            // Discovery is synchronous and may fail after its request was canceled.
                            if (currentRevision == revision && !stopping.IsCancellationRequested) Publish("Error", ex.Message);
                        }
                    }
                    finally {
                        lock (gate) { request.Dispose(); request = null; }
                    }
                    try { await Task.Delay(interval, stopping.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            } finally { observationStore?.Dispose(); observationStore = null; }
        }
        private Store observationStore;
        private void Observe(bool asleep, ref SleepIntervalTracker tracker)
        {
            if (observationStore == null) observationStore = new Store(database);
            if (tracker == null) tracker = new SleepIntervalTracker(observationStore);
            if (tracker.Observe(asleep, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) {
                try { DataChanged?.Invoke(); } catch { }
            }
        }
        private void Publish(string state, string detail = "")
        {
            if (status.State == state && status.Detail == detail) return;
            status = new OyasumiSleepStatus(state, detail);
            try { StatusChanged?.Invoke(status); } catch { }
        }
    }
}
