using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed partial class HeartChart
    {
        internal static List<long> MidnightBoundaries(long from, long to, TimeZoneInfo zone)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            var result = new List<long>();
            if (to <= from) return result;
            var first = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(from), zone).Date;
            var last = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(to), zone).Date;
            for (var day = first; day <= last; day = day.AddDays(1)) {
                long midnight = Store.SleepDayBoundary(day, zone, 0);
                if (midnight > from && midnight < to && (result.Count == 0 || result[result.Count - 1] != midnight)) result.Add(midnight);
                if (day == DateTime.MaxValue.Date) break;
            }
            return result;
        }

        private void DrawMidnightSeparators(DrawingContext dc, double left, double top, double width, double height)
        {
            if (!ShowMidnightSeparators || To <= From) return;
            var boundaries = MidnightBoundaries(From, To, TimeZoneInfo.Local);
            var pen = FrozenPen(ink, 1.3, DashStyles.Dash);
            double lastLabelRight = double.NegativeInfinity;
            double lastLineX = double.NegativeInfinity;
            foreach (long midnight in boundaries) {
                double x = left + (midnight - From) / (double)(To - From) * width;
                if (x - lastLineX < 4) continue;
                dc.DrawLine(pen, new Point(x, top), new Point(x, top + height));
                lastLineX = x;
                var label = new FormattedText(DateTimeOffset.FromUnixTimeMilliseconds(midnight).LocalDateTime.ToString("yyyy/MM/dd") + " 00:00",
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Yu Gothic UI"), 10, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double labelX = Math.Max(left, Math.Min(left + width - label.Width - 8, x + 4));
                if (labelX < lastLabelRight + 8) continue;
                dc.DrawRectangle(TryFindResource("Surface") as Brush ?? Brushes.White, null, new Rect(labelX, top + 2, label.Width + 8, label.Height + 4));
                dc.DrawText(label, new Point(labelX + 4, top + 4));
                lastLabelRight = labelX + label.Width + 8;
            }
        }
    }
}
