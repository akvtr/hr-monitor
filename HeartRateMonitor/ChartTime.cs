using System;
using System.Collections.Generic;
using System.Globalization;

namespace HeartRateMonitor
{
    public static class ChartTime
    {
        public static List<long> Ticks(long from, long to, double pixels)
        {
            var ticks = new List<long> { from };
            if (to <= from) return ticks;
            long span = to - from;
            double desired = span / Math.Max(2, Math.Min(8, pixels / 130));
            long step = 21600000;
            foreach (long candidate in new long[] { 1000, 5000, 10000, 15000, 30000, 60000, 120000, 300000, 600000, 900000, 1800000, 3600000, 7200000, 10800000, 21600000 })
                if (candidate >= desired) { step = candidate; break; }
            long offset = (long)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(from)).TotalMilliseconds;
            long first = ((long)Math.Floor((from + offset) / (double)step) + 1) * step - offset;
            for (long t = first; t < to; t += step) {
                if ((t - ticks[ticks.Count - 1]) * pixels / span >= 95 && (to - t) * pixels / span >= 95) ticks.Add(t);
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
