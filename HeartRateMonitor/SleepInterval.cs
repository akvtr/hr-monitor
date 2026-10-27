namespace HeartRateMonitor
{
    public sealed class SleepInterval
    {
        public long Id { get; set; }
        public long From { get; set; }
        public long To { get; set; }
        public bool Excluded { get; set; }
        public bool IsOngoing { get; set; }
    }
}
