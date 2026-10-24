using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HeartRateMonitor
{
    public sealed class OscTransmission
    {
        public DateTime Time = DateTime.Now;
        public string Endpoint, IntAddress, FloatAddress;
        public int Integer;
        public float Percent;
        public bool Test;
    }
    public sealed class RecordingEngine
    {
        private enum WorkKind { Reading, OscTest, Retention }
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
        private volatile Settings settings;
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
            settings = initial.Copy();
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
                    var remaining = nextRetention - retentionClock.Elapsed;
                    if (remaining <= TimeSpan.Zero) {
                        nextRetention = retentionClock.Elapsed + retentionInterval;
                        PruneExpired(store);
                        continue;
                    }
                    Work work;
                    int waitMilliseconds = (int)Math.Ceiling(Math.Min(30000, remaining.TotalMilliseconds));
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
                default: throw new InvalidOperationException("Unknown recording work kind.");
            }
        }
        private void ProcessOscTest(OscSender osc, Work work)
        {
            try {
                Send(osc, work.Configuration, work.TestBpm, true);
                work.Completion.SetResult(true);
            } catch (Exception ex) {
                OscFailed?.Invoke(ex.Message);
                work.Completion.SetException(ex);
            }
        }
        private void ProcessReading(Store store, OscSender osc, Reading reading)
        {
            string storage = SaveReading(store, reading);
            string sending = SendReading(osc, reading);
            Processed?.Invoke(reading, storage, sending);
        }
        private string SaveReading(Store store, Reading reading)
        {
            try { store.Add(reading); return "saved"; }
            catch (Exception ex) { Error?.Invoke(ex.Message); return ex.Message; }
        }
        private string SendReading(OscSender osc, Reading reading)
        {
            var current = settings;
            try {
                if (!current.OscEnabled) return "disabled";
                if (reading.Bpm <= 0 || reading.Contact == false) return "waiting";
                Send(osc, current, reading.Bpm, false);
                return "sent";
            } catch (Exception ex) { OscFailed?.Invoke(ex.Message); return "error"; }
        }
        private void PruneExpired(Store store)
        {
            try { if (DeleteExpired(store) > 0) RetentionChanged?.Invoke(); }
            catch (Exception ex) { Error?.Invoke(Words.T("古い記録の削除に失敗しました: ", "Could not delete expired recordings: ") + ex.Message); }
        }
        private int DeleteExpired(Store store)
        {
            var cutoff = settings.RetentionCutoff(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return cutoff.HasValue ? store.DeleteBefore(cutoff.Value) : 0;
        }
        private void Send(OscSender sender, Settings config, int bpm, bool test)
        {
            if (!config.OscEnabled) throw new InvalidOperationException(Words.T("設定でOSC送信を有効にして保存してください。", "Enable OSC in Settings and save first."));
            var values = new OscValues(config, bpm);
            sender.Send(config, values);
            Transmitted?.Invoke(new OscTransmission { Endpoint = config.Host + ":" + config.Port, IntAddress = values.IntAddress, FloatAddress = values.FloatAddress, Integer = values.Integer, Percent = values.Percent, Test = test });
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
            value.Validate();
            bool retentionChanged = settings.RetentionDays != value.RetentionDays;
            settings = value.Copy();
            if (retentionChanged) {
                // Wake the writer to apply the latest policy, including while receiving no data.
                // If the queue is full, its regular deadline still applies within one minute.
                try { queue.TryAdd(new Work { Kind = WorkKind.Retention }); }
                catch (InvalidOperationException) { }
            }
        }
        public void Accept(Reading reading)
        {
            try { if (!queue.TryAdd(new Work { Kind = WorkKind.Reading, Reading = reading })) Error?.Invoke(Words.T("保存キューが満杯です。この通知は保存できませんでした。", "The recording queue is full. This notification was not saved.")); }
            catch (InvalidOperationException) { }
        }
        public async Task StopAsync() { queue.CompleteAdding(); await worker; queue.Dispose(); }
    }
}
