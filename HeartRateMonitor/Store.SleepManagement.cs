using System;
using System.Collections.Generic;
using System.Linq;

namespace HeartRateMonitor
{
    public sealed partial class Store
    {
        private bool sleepManagementReady;
        private void EnsureSleepManagement()
        {
            if (sleepManagementReady) return;
            Execute("CREATE TABLE IF NOT EXISTS sleep_management(sleep_id INTEGER PRIMARY KEY,excluded INTEGER NOT NULL DEFAULT 0,ongoing INTEGER NOT NULL DEFAULT 0);");
            Execute("CREATE TRIGGER IF NOT EXISTS sleep_management_cleanup AFTER DELETE ON sleep_intervals BEGIN DELETE FROM sleep_management WHERE sleep_id=OLD.id; END;");
            sleepManagementReady = true;
        }
        // Only the recording owner calls this at startup or when replacing its tracker.
        public void CloseStaleSleepIntervals()
        {
            lock (gate) { EnsureSleepManagement(); Execute("UPDATE sleep_management SET ongoing=0 WHERE ongoing<>0;"); }
        }
        public bool SetSleepExcluded(long id, bool excluded)
        {
            lock (gate) {
                EnsureSleepManagement();
                var s = Prepare("INSERT OR REPLACE INTO sleep_management(sleep_id,excluded,ongoing) SELECT id,?,0 FROM sleep_intervals WHERE id=? AND NOT EXISTS(SELECT 1 FROM sleep_management WHERE sleep_id=? AND ongoing<>0);");
                try {
                    BindInt(s, 1, excluded ? 1 : 0); BindInt(s, 2, id); BindInt(s, 3, id);
                    int rc = Native.sqlite3_step(s); if (rc != Native.Done) Check(rc);
                    return Native.sqlite3_changes(db) != 0;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        // Boundaries are converted separately, preserving actual elapsed time through DST changes.
        public static long SleepDayBoundary(DateTime day, TimeZoneInfo zone, int boundaryHour)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (boundaryHour < 0 || boundaryHour > 23) throw new ArgumentOutOfRangeException(nameof(boundaryHour));
            DateTime local = DateTime.SpecifyKind(day.Date.AddHours(boundaryHour), DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUnixTimeMilliseconds();
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)).ToUnixTimeMilliseconds();
        }
        public SleepAnalysis LoadSleepAnalysis(DateTime firstDay, DateTime lastDay, TimeZoneInfo zone, int boundaryHour = 12)
        {
            firstDay = firstDay.Date; lastDay = lastDay.Date;
            if (lastDay < firstDay || (lastDay - firstDay).TotalDays > 366) throw new ArgumentOutOfRangeException(nameof(lastDay));
            long from = SleepDayBoundary(firstDay, zone, boundaryHour), to = SleepDayBoundary(lastDay.AddDays(1), zone, boundaryHour);
            lock (gate) { EnsureSleepManagement(); }
            return ReadSnapshot(() => {
                var result = new SleepAnalysis();
                var included = new List<SleepInterval>();
                foreach (var interval in SleepIntervals(from, to)) {
                    var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(interval.From), zone).DateTime;
                    result.Records.Add(new SleepRecord { Interval = interval, Summary = Summary(interval.From, interval.To), Day = local.AddHours(-boundaryHour).Date });
                    if (!interval.Excluded && !interval.IsOngoing) included.Add(interval);
                }
                result.IncludedCount = included.Count;
                var starts = new List<double>(); var ends = new List<double>();
                double totalDuration = 0, heartTotal = 0;
                for (DateTime day = firstDay; day <= lastDay; day = day.AddDays(1)) {
                    long begin = SleepDayBoundary(day, zone, boundaryHour), end = SleepDayBoundary(day.AddDays(1), zone, boundaryHour);
                    var item = new SleepDay { Date = day };
                    foreach (var interval in included) {
                        long clippedFrom = Math.Max(begin, interval.From), clippedTo = Math.Min(end, interval.To);
                        if (clippedTo > clippedFrom) item.Fragments.Add(new SleepFragment { IntervalId = interval.Id, From = clippedFrom, To = clippedTo });
                    }
                    // Union intervals so overlapping stored records never double-count time or samples.
                    var ranges = new List<TimeWindow>();
                    foreach (var fragment in item.Fragments.OrderBy(f => f.From).ThenBy(f => f.To)) {
                        if (ranges.Count == 0 || fragment.From > ranges[ranges.Count - 1].To) ranges.Add(new TimeWindow(fragment.From, fragment.To));
                        else {
                            var previous = ranges[ranges.Count - 1];
                            ranges[ranges.Count - 1] = new TimeWindow(previous.From, Math.Max(previous.To, fragment.To));
                        }
                    }
                    long dayCount = 0;
                    foreach (var range in ranges) {
                        item.DurationMilliseconds += range.To - range.From;
                        var summary = Summary(range.From, range.To);
                        dayCount += summary.Count; result.ValidReadingCount += summary.Count; heartTotal += summary.Average * summary.Count;
                    }
                    result.Days.Add(item);
                    if (item.DurationMilliseconds == 0) continue;
                    result.RecordedDays++; if (dayCount > 0) result.HeartRateDays++;
                    totalDuration += item.DurationMilliseconds;
                    result.ShortestDurationMilliseconds = Math.Min(result.ShortestDurationMilliseconds ?? long.MaxValue, item.DurationMilliseconds);
                    result.LongestDurationMilliseconds = Math.Max(result.LongestDurationMilliseconds ?? 0, item.DurationMilliseconds);
                    // The longest continuous observed range represents the principal sleep for the day.
                    var main = ranges.OrderByDescending(r => r.To - r.From).ThenBy(r => r.From).First();
                    starts.Add(SleepClockMinutes(main.From, day, zone, boundaryHour));
                    ends.Add(SleepClockMinutes(main.To, day, zone, boundaryHour));
                }
                if (result.RecordedDays > 0) {
                    result.AverageDurationMilliseconds = totalDuration / result.RecordedDays;
                    result.TypicalStartMinutes = SleepMedian(starts); result.TypicalEndMinutes = SleepMedian(ends);
                }
                if (result.ValidReadingCount > 0) result.AverageHeartRate = heartTotal / result.ValidReadingCount;
                return result;
            });
        }
        private static double SleepClockMinutes(long time, DateTime day, TimeZoneInfo zone, int boundaryHour)
        {
            return (TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(time), zone).DateTime - day.AddHours(boundaryHour)).TotalMinutes;
        }
        private static double SleepMedian(List<double> values)
        {
            values.Sort(); int middle = values.Count / 2;
            return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
        }
    }
}
