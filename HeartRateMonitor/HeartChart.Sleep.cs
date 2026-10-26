using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed partial class HeartChart
    {
        private void DrawSleepIntervals(DrawingContext dc, double left, double top, double width, double height)
        {
            foreach (var item in SleepIntervals) {
                if (item.From >= To || item.To <= From) continue;
                double start = left + (Math.Max(From, item.From) - From) / (double)Math.Max(1, To - From) * width;
                double end = left + (Math.Min(To, item.To) - From) / (double)Math.Max(1, To - From) * width;
                var fill = accent.CloneCurrentValue(); fill.Opacity = 0.08;
                dc.DrawRectangle(fill, null, new Rect(start, top, Math.Max(1, end - start), height));
                dc.DrawLine(FrozenPen(accent, 2, DashStyles.Dash), new Point(start, top), new Point(end, top));
                var text = new FormattedText(Words.T("睡眠 · OyasumiVR", "Sleep · OyasumiVR"), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI, Yu Gothic UI"), 11,
                    TryFindResource("Text") as Brush ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                text.MaxTextWidth = Math.Max(1, left + width - start - 4); text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawRectangle(TryFindResource("Surface") as Brush ?? Brushes.White, null, new Rect(start, top + 1, text.Width + 4, text.Height));
                dc.DrawText(text, new Point(start + 2, top + 1));
            }
        }
    }
}
