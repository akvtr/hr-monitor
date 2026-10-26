using System;
using System.Collections.Generic;

namespace HeartRateMonitor
{
    public static class HistorySelection
    {
        public const long MinimumWidth = 60000;
        public static TimeWindow Zoom(TimeWindow domain, TimeWindow selection, int wheelDelta, double anchor)
        {
            selection = Clamp(domain, selection);
            anchor = Math.Max(0, Math.Min(1, anchor));
            long oldWidth = selection.To - selection.From;
            double proposed = oldWidth * Math.Pow(0.8, wheelDelta / 120.0);
            long width = (long)Math.Round(Math.Min(domain.To - domain.From, Math.Max(Math.Min(MinimumWidth, domain.To - domain.From), proposed)));
            long from = selection.From + (long)Math.Round((oldWidth - width) * anchor);
            return Clamp(domain, new TimeWindow(from, from + width));
        }
        public static TimeWindow SelectedInterval(TimeWindow view, double start, double end)
        {
            start = Math.Max(0, Math.Min(1, start)); end = Math.Max(0, Math.Min(1, end));
            long from = view.From + (long)((view.To - view.From) * Math.Min(start, end));
            long to = view.From + (long)((view.To - view.From) * Math.Max(start, end));
            return to > from ? new TimeWindow(from, to) : null;
        }
        public static TimeWindow Clamp(TimeWindow domain, TimeWindow selection)
        {
            long width = Math.Min(domain.To - domain.From, Math.Max(MinimumWidth, selection.To - selection.From));
            long from = Math.Max(domain.From, Math.Min(domain.To - width, selection.From));
            return new TimeWindow(from, from + width);
        }
        public static TimeWindow Move(TimeWindow domain, TimeWindow selection, long delta)
        {
            selection = Clamp(domain, selection);
            long width = selection.To - selection.From;
            // Bound the shift before adding it so extreme deltas cannot wrap the timestamp.
            long shift = Math.Max(domain.From - selection.From, Math.Min(domain.To - width - selection.From, delta));
            long from = selection.From + shift;
            return new TimeWindow(from, from + width);
        }
        public static TimeWindow Resize(TimeWindow domain, TimeWindow selection, bool left, long time)
        {
            selection = Clamp(domain, selection);
            long minimum = Math.Min(MinimumWidth, domain.To - domain.From);
            return left ? new TimeWindow(Math.Max(domain.From, Math.Min(selection.To - minimum, time)), selection.To)
                : new TimeWindow(selection.From, Math.Min(domain.To, Math.Max(selection.From + minimum, time)));
        }
        // A bounded preview derived exclusively from chronological recorded bucket averages.
        // Exact statistics are queried separately after interaction ends.
        public static List<ChartPoint> Preview(List<ChartPoint> source, TimeWindow range, int maximum = 400)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (range == null) throw new ArgumentNullException(nameof(range));
            var result = new List<ChartPoint>();
            long width = ChartBuckets.Width(range.From, range.To, maximum), group = -1;
            long? previous = null;
            ChartPoint aggregate = null; int count = 0;
            foreach (var point in source) {
                if (point == null || (previous.HasValue && point.UnixMs < previous.Value))
                    throw new ArgumentException("Preview requires non-null chart points in chronological order.", nameof(source));
                previous = point.UnixMs;
                if (point.UnixMs < range.From || point.UnixMs >= range.To) continue;
                long next = (point.UnixMs - range.From) / width;
                if (aggregate == null || next != group) {
                    if (aggregate != null) aggregate.Bpm /= count;
                    aggregate = new ChartPoint { UnixMs = point.UnixMs, Min = point.Min, Max = point.Max };
                    result.Add(aggregate); group = next; count = 0;
                }
                aggregate.Bpm += point.Bpm; count++;
                aggregate.Min = Math.Min(aggregate.Min, point.Min); aggregate.Max = Math.Max(aggregate.Max, point.Max);
            }
            if (aggregate != null) aggregate.Bpm /= count;
            return result;
        }
    }
}
