using System;

namespace HeartRateMonitor
{
    public sealed class TimeWindow
    {
        public long From { get; }
        public long To { get; }
        public TimeWindow(long from, long to)
        {
            if (to <= from) throw new ArgumentException("Invalid time range.");
            From = from; To = to;
        }
    }
}
