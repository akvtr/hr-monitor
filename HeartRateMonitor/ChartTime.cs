using System;
using System.Collections.Generic;
using System.Globalization;

namespace HeartRateMonitor
{
    public static class ChartTime
    {
        private const double MinimumTickSpacing = 95;
        private static readonly long[] tickSteps = { 1000, 5000, 10000, 15000, 30000, 60000, 120000, 300000, 600000, 900000, 1800000, 3600000, 7200000, 10800000, 21600000 };
        public static List<long> Ticks(long from, long to, double pixels)
        {
            var ticks = new List<long> { from };
            if (to <= from) return ticks;
            if (pixels < 2 * MinimumTickSpacing || double.IsNaN(pixels) || double.IsInfinity(pixels)) { ticks.Add(to); return ticks; }
            long span = to - from;
            double desired = span / Math.Max(2, Math.Min(8, pixels / 130));
            long step = tickSteps[tickSteps.Length - 1];
            foreach (long candidate in tickSteps)
                if (candidate >= desired) { step = candidate; break; }
            long offset = (long)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(from)).TotalMilliseconds;
            long first = ((long)Math.Floor((from + offset) / (double)step) + 1) * step - offset;
            // Skip candidates that cannot fit, leaving a grid-step margin for rounding at the boundary.
            long stride = Math.Max(1, (long)Math.Floor(MinimumTickSpacing * span / pixels / step) - 1);
            for (long t = first + (stride - 1) * step; t < to;) {
                if ((to - t) * pixels / span < MinimumTickSpacing) break;
                if ((t - ticks[ticks.Count - 1]) * pixels / span >= MinimumTickSpacing) {
                    ticks.Add(t);
                    t += stride * step;
                } else t += step;
            }
            ticks.Add(to); return ticks;
        }
        public static string Label(long time, long span) => DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.ToString(span <= 1200000 ? "HH:mm:ss" : "HH:mm");
        public static string Duration(long milliseconds)
        {
            long hours = milliseconds / 3600000, minutes = milliseconds / 60000 % 60;
            string seconds = (milliseconds % 60000 / 1000.0).ToString("0.###", CultureInfo.CurrentCulture);
            return Words.Language == "en"
                ? (hours > 0 ? hours + "h " : "") + (hours > 0 || minutes > 0 ? minutes + "m " : "") + seconds + "s"
                : (hours > 0 ? hours + "時間" : "") + (hours > 0 || minutes > 0 ? minutes + "分" : "") + seconds + "秒";
        }
    }
}
