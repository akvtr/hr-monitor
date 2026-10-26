using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HeartRateMonitor
{
    public sealed class RecordingEngine
    {
        private enum WorkKind { Reading, OscTest, Retention, Configure }
        private sealed class Work
        {
            public WorkKind Kind;
            public Reading Reading;
            public int TestBpm;
            public TaskCompletionSource<bool> Completion;
            public Settings Configuration;
        }
        private readonly BlockingCollection<Work> queue = new BlockingCollection<Work>(4096);
        private readonly Task worker;
        private readonly object stopGate = new object();
        private Task stopping;
        private volatile Settings settings;
        private readonly ReceptionTimer receptionTimer = new ReceptionTimer();
        private bool timeoutSent;
        private Settings timeoutConfiguration;
        public event Action<Reading, string, string> Processed;
        public event Action<OscTransmission> Transmitted;
        public event Action<string> Error;
        public event Action<string> OscFailed;
        public event Action RetentionChanged;
        public string StartupRetentionError { get; private set; }
        public RecordingEngine(string database, Settings initial)
            : this(database, initial, TimeSpan.FromMinutes(1)) { }
        internal RecordingEngine(string database, Settings initial, TimeSpan retentionInterval)
        {
            if (retentionInterval <= TimeSpan.Zero || retentionInterval > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(retentionInterval));
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            settings = initial.Copy();
            settings.Validate();
            var store = new Store(database);
            var retentionClock = Stopwatch.StartNew();
            try { DeleteExpired(store); }
            catch (Exception ex) { StartupRetentionError = ex.Message; }
            worker = Task.Run(() => RunWorker(store, retentionClock, retentionInterval));
        }
        private void RunWorker(Store store, Stopwatch retentionClock, TimeSpan retentionInterval)
        {
            using (store)
            using (var osc = new OscSender()) {
                var nextRetention = retentionInterval;
                while (true) {
                    if (!queue.IsCompleted) CheckOscTimeout(osc);
                    var remaining = nextRetention - retentionClock.Elapsed;
                    if (remaining <= TimeSpan.Zero) {
                        nextRetention = retentionClock.Elapsed + retentionInterval;
                        PruneExpired(store);
                        continue;
                    }
                    Work work;
                    int pollMilliseconds = settings.OscEnabled && settings.OscZeroOnTimeout ? 100 : 30000;
                    int waitMilliseconds = (int)Math.Ceiling(Math.Min(pollMilliseconds, remaining.TotalMilliseconds));
                    if (!queue.TryTake(out work, waitMilliseconds)) {
                        if (queue.IsCompleted) break;
                        continue;
                    }
                    ProcessWork(store, osc, work);
                }
            }
        }
        private void ProcessWork(Store store, OscSender osc, Work work)
        {
            switch (work.Kind) {
                case WorkKind.Reading: ProcessReading(store, osc, work.Reading); break;
                case WorkKind.OscTest: ProcessOscTest(osc, work); break;
                case WorkKind.Retention: PruneExpired(store); break;
                case WorkKind.Configure: break;
                default: throw new InvalidOperationException("Unknown recording work kind.");
            }
        }
        private void ProcessOscTest(OscSender osc, Work work)
        {
            try {
                Send(osc, work.Configuration, work.TestBpm, true);
                timeoutSent = false;
                work.Completion.SetResult(true);
            } catch (Exception ex) {
                NotifyObservers(OscFailed, handler => handler(ex.Message));
                work.Completion.SetException(ex);
            }
        }
        private void ProcessReading(Store store, OscSender osc, Reading reading)
        {
            timeoutSent = false;
            string storage = SaveReading(store, reading);
            string sending = SendReading(osc, reading);
            NotifyObservers(Processed, handler => handler(reading, storage, sending));
        }
        private string SaveReading(Store store, Reading reading)
        {
            try { store.Add(reading); return "saved"; }
            catch (Exception ex) { NotifyObservers(Error, handler => handler(ex.Message), reportErrors: false); return ex.Message; }
        }
        private string SendReading(OscSender osc, Reading reading)
        {
            var current = settings;
            try {
                if (!current.OscEnabled) return "disabled";
                if (reading.Bpm <= 0 || reading.Contact == false) return "waiting";
                Send(osc, current, reading.Bpm, false);
                return "sent";
            } catch (Exception ex) { NotifyObservers(OscFailed, handler => handler(ex.Message)); return "error"; }
        }
        private void PruneExpired(Store store)
        {
            try { if (DeleteExpired(store) > 0) NotifyObservers(RetentionChanged, handler => handler()); }
            catch (Exception ex) { NotifyObservers(Error, handler => handler(Words.T("古い記録の削除に失敗しました: ", "Could not delete expired recordings: ") + ex.Message), reportErrors: false); }
        }
        private int DeleteExpired(Store store)
        {
            var cutoff = settings.RetentionCutoff(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return cutoff.HasValue ? store.DeleteBefore(cutoff.Value) : 0;
        }
        private void CheckOscTimeout(OscSender osc)
        {
            var current = settings;
            var previous = timeoutConfiguration;
            if (previous == null || previous.OscEnabled != current.OscEnabled || previous.OscZeroOnTimeout != current.OscZeroOnTimeout ||
                previous.OscTimeoutSeconds != current.OscTimeoutSeconds || previous.Host != current.Host || previous.Port != current.Port ||
                previous.IntParameter != current.IntParameter || previous.FloatParameter != current.FloatParameter)
                timeoutSent = false;
            timeoutConfiguration = current;
            if (!current.OscEnabled || !current.OscZeroOnTimeout || timeoutSent || queue.Count > 0) return;
            if (receptionTimer.ElapsedSeconds < current.OscTimeoutSeconds) return;
            // One attempt per silent period; a new reading or relevant configuration change rearms it.
            timeoutSent = true;
            try { Send(osc, current, 0, false, true); }
            catch (Exception ex) { NotifyObservers(OscFailed, handler => handler(ex.Message)); }
        }
        private void Send(OscSender sender, Settings config, int bpm, bool test, bool timeout = false)
        {
            if (!config.OscEnabled) throw new InvalidOperationException(Words.T("設定でOSC送信を有効にして保存してください。", "Enable OSC in Settings and save first."));
            var values = new OscValues(config, bpm);
            sender.Send(config, values);
            var transmission = new OscTransmission { Endpoint = config.Host + ":" + config.Port, IntAddress = values.IntAddress, FloatAddress = values.FloatAddress, Integer = values.Integer, Percent = values.Percent, Test = test, Timeout = timeout };
            NotifyObservers(Transmitted, handler => handler(transmission));
        }
        private void NotifyObservers<THandler>(THandler observers, Action<THandler> notify, bool reportErrors = true) where THandler : Delegate
        {
            if (observers == null) return;
            foreach (THandler observer in observers.GetInvocationList()) {
                try { notify(observer); }
                catch (Exception ex) {
                    // Error observers are isolated too; do not recursively report their failures.
                    if (reportErrors) NotifyObservers(Error, handler => handler(Words.T("通知処理に失敗しました: ", "Notification handler failed: ") + ex.Message), reportErrors: false);
                }
            }
        }
        public Task TestAsync(int bpm)
        {
            if (bpm < 1 || bpm > 65535) throw new ArgumentOutOfRangeException(nameof(bpm));
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try { if (!queue.TryAdd(new Work { Kind = WorkKind.OscTest, TestBpm = bpm, Completion = completion, Configuration = settings.Copy() })) completion.SetException(new InvalidOperationException("Queue is full.")); }
            catch (InvalidOperationException ex) { completion.SetException(ex); }
            return completion.Task;
        }
        public void Configure(Settings value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var next = value.Copy();
            next.Validate();
            bool retentionChanged = settings.RetentionDays != next.RetentionDays;
            settings = next;
            // Both work kinds wake the writer. A policy change also prunes expired readings.
            // If the queue is full, its regular deadline still applies within one minute.
            try { queue.TryAdd(new Work { Kind = retentionChanged ? WorkKind.Retention : WorkKind.Configure }); }
            catch (InvalidOperationException) { }
        }
        public void Accept(Reading reading)
        {
            if (reading == null) throw new ArgumentNullException(nameof(reading));
            receptionTimer.Reset();
            try { if (!queue.TryAdd(new Work { Kind = WorkKind.Reading, Reading = reading.Copy() })) NotifyObservers(Error, handler => handler(Words.T("保存キューが満杯です。この通知は保存できませんでした。", "The recording queue is full. This notification was not saved.")), reportErrors: false); }
            catch (InvalidOperationException) { }
        }
        public Task StopAsync()
        {
            lock (stopGate) {
                if (stopping == null) {
                    queue.CompleteAdding();
                    stopping = DrainAsync();
                }
                return stopping;
            }
        }
        private async Task DrainAsync()
        {
            try { await worker.ConfigureAwait(false); }
            finally { queue.Dispose(); }
        }
    }
}
