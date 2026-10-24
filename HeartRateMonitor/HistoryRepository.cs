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
                var bounds = store.Bounds();
                return new HistoryOverview(bounds,
                    bounds == null ? new List<ChartPoint>() : store.Chart(bounds.From, bounds.To, 14400),
                    bounds == null ? new List<ChartPoint>() : store.Chart(bounds.From, bounds.To, 1200));
            }
        }

        internal HistoryDetail LoadWindow(TimeWindow range)
        {
            using (var store = new Store(database))
                return new HistoryDetail(store.Summary(range.From, range.To), store.Chart(range.From, range.To));
        }

        internal HistorySummary Summarize(TimeWindow range)
        {
            using (var store = new Store(database)) return store.Summary(range.From, range.To);
        }

        internal List<DailyActivity> LoadActivity(DateTime today, TimeZoneInfo zone)
        {
            using (var store = new Store(database)) return store.DailyActivity(today.Date.AddDays(-364), today.Date, zone);
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

    internal sealed class HistoryDetail
    {
        internal HistorySummary Summary { get; }
        internal List<ChartPoint> Points { get; }

        internal HistoryDetail(HistorySummary summary, List<ChartPoint> points)
        { Summary = summary; Points = points; }
    }
}
