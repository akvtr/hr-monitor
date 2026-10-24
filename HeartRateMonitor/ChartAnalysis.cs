using System;
using System.Collections.Generic;

namespace HeartRateMonitor
{
    public static class ChartAnalysis
    {
        public static HistorySummary SummarizeRaw(IEnumerable<ChartPoint> samples, TimeWindow range)
        {
            var summary = new HistorySummary(); double sum = 0;
            foreach (var sample in samples) {
                if (sample.UnixMs < range.From || sample.UnixMs >= range.To || sample.Bpm <= 0) continue;
                if (sample.Min != sample.Max || sample.Bpm != sample.Min || sample.Bpm != Math.Truncate(sample.Bpm))
                    throw new ArgumentException("Interval statistics require original readings, not chart buckets.");
                int bpm = (int)sample.Bpm;
                summary.Min = summary.Count == 0 ? bpm : Math.Min(summary.Min, bpm);
                summary.Max = summary.Count == 0 ? bpm : Math.Max(summary.Max, bpm);
                summary.Count++; sum += bpm;
            }
            summary.Average = summary.Count > 0 ? sum / summary.Count : 0;
            return summary;
        }
    }
}
