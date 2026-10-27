using System;
using System.Collections.Generic;

namespace HeartRateMonitor
{
    public sealed class SleepRecord
    {
        public SleepInterval Interval;
        public HistorySummary Summary;
        public DateTime Day;
        public long DurationMilliseconds => Interval.To - Interval.From;
    }
    public sealed class SleepFragment
    {
        public long IntervalId;
        public long From, To;
    }
    public sealed class SleepDay
    {
        public DateTime Date;
        public List<SleepFragment> Fragments = new List<SleepFragment>();
        public long DurationMilliseconds;
    }
    public sealed class SleepAnalysis
    {
        public List<SleepRecord> Records = new List<SleepRecord>();
        public List<SleepDay> Days = new List<SleepDay>();
        public double? AverageDurationMilliseconds, AverageHeartRate, TypicalStartMinutes, TypicalEndMinutes;
        public int RecordedDays, HeartRateDays, IncludedCount;
        public long ValidReadingCount;
        public long? ShortestDurationMilliseconds, LongestDurationMilliseconds;
    }
}
