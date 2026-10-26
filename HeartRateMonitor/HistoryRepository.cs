using System;
using System.Collections.Generic;

namespace HeartRateMonitor
{
    // Each operation owns its connection so background reads never share a UI-owned Store.
    internal sealed class HistoryRepository
    {
        private readonly string database;

        internal HistoryRepository(string database) { this.database = database; }

        internal HistoryOverview LoadOverview()
        {
            using (var store = new Store(database)) {
                return store.ReadSnapshot(() => {
                    var bounds = store.HistoryBounds();
                    return new HistoryOverview(bounds,
                        bounds == null ? new List<ChartPoint>() : store.Chart(bounds.From, bounds.To, ChartBuckets.MaximumPoints),
                        bounds == null ? new List<ChartPoint>() : store.Chart(bounds.From, bounds.To, 1200));
                });
            }
        }

        internal HistoryDetail LoadWindow(TimeWindow range)
        {
            using (var store = new Store(database))
                return store.ReadSnapshot(() => new HistoryDetail(store.Summary(range.From, range.To), store.Chart(range.From, range.To), store.SleepIntervals(range.From, range.To)));
        }

        internal HistorySummary Summarize(TimeWindow range)
        {
            using (var store = new Store(database)) return store.Summary(range.From, range.To);
        }

        internal HistorySleepWindow LoadSleepWindow(TimeWindow range)
        {
            using (var store = new Store(database))
                return store.ReadSnapshot(() => new HistorySleepWindow(store.HistoryBounds(), store.SleepIntervals(range.From, range.To)));
        }

        internal List<DailyActivity> LoadActivity(DateTime month, TimeZoneInfo zone)
        {
            var first = new DateTime(month.Year, month.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            using (var store = new Store(database))
                return store.ReadSnapshot(() => store.DailyActivity(first, last, zone));
        }

        internal long Export(string path, TimeWindow range)
        {
            using (var store = new Store(database)) return store.Export(path, range.From, range.To);
        }
    }

    internal sealed class HistoryOverview
    {
        internal TimeWindow Bounds { get; }
        internal List<ChartPoint> Preview { get; }
        internal List<ChartPoint> Overview { get; }

        internal HistoryOverview(TimeWindow bounds, List<ChartPoint> preview, List<ChartPoint> overview)
        { Bounds = bounds; Preview = preview; Overview = overview; }
    }

    internal sealed class HistorySleepWindow
    {
        internal TimeWindow Bounds { get; }
        internal List<SleepInterval> Intervals { get; }
        internal HistorySleepWindow(TimeWindow bounds, List<SleepInterval> intervals) { Bounds = bounds; Intervals = intervals; }
    }

    internal sealed class HistoryDetail
    {
        internal HistorySummary Summary { get; }
        internal List<ChartPoint> Points { get; }
        internal List<SleepInterval> SleepIntervals { get; }

        internal HistoryDetail(HistorySummary summary, List<ChartPoint> points, List<SleepInterval> sleepIntervals = null)
        { Summary = summary; Points = points; SleepIntervals = sleepIntervals ?? new List<SleepInterval>(); }
    }
}
