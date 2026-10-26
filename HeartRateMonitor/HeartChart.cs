using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed partial class HeartChart : FrameworkElement
    {
        public List<ChartPoint> Points = new List<ChartPoint>();
        public List<SleepInterval> SleepIntervals = new List<SleepInterval>();
        public long From;
        public long To;
        public long GapMs = 15000;
        public double? Average;
        private Point pointer;
        private bool pointerInside;
        private Brush ink => TryFindResource("TextMuted") as Brush ?? Brushes.Gray;
        private Brush accent => TryFindResource("Accent") as Brush ?? Brushes.Teal;
        public HeartChart()
        {
            MinHeight = 200; ClipToBounds = true; Cursor = Cursors.Cross;
            MouseMove += (s, e) => { pointer = e.GetPosition(this); pointerInside = true; InvalidateVisual(); };
            MouseLeave += (s, e) => { pointerInside = false; InvalidateVisual(); };
            InitializeInteraction();
        }
        private void Text(DrawingContext dc, string text, double x, double y) {
            dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        }
        private static Pen FrozenPen(Brush brush, double thickness, DashStyle dashStyle = null)
        {
            // Reusing an unfrozen Pen across thousands of drawings makes WPF's
            // change tracking expensive. Freeze a snapshot, leaving theme resources alone.
            var pen = new Pen(brush.IsFrozen ? brush : brush.CloneCurrentValue(), thickness);
            if (dashStyle != null) pen.DashStyle = dashStyle;
            pen.Freeze();
            return pen;
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            // Make empty areas part of the hit-test surface. Without a background
            // drawing WPF only raises mouse events over the visible grid/data lines.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(new Point(0, 0), RenderSize));
            double left = 42, top = 18, width = ActualWidth - 60, height = ActualHeight - 70;
            if (width < 10 || height < 10) return;
            var valid = Points.Where(p => p.UnixMs >= From && p.UnixMs <= To).ToList();
            double min = valid.Count == 0 ? 40 : Math.Max(0, Math.Floor((valid.Min(p => p.Min) - 10) / 20) * 20);
            double max = valid.Count == 0 ? 160 : Math.Max(min + 40, Math.Ceiling((valid.Max(p => p.Max) + 10) / 20) * 20);
            Func<long, double> x = t => left + (t - From) / (double)Math.Max(1, To - From) * width;
            Func<double, double> y = v => top + (max - v) / (max - min) * height;
            var grid = FrozenPen(TryFindResource("ChartGrid") as Brush ?? Brushes.LightGray, 0.7);
            DrawSleepIntervals(dc, left, top, width, height);
            for (int i = 0; i <= 4; i++) {
                double value = min + (max - min) * i / 4;
                dc.DrawLine(grid, new Point(left, y(value)), new Point(left + width, y(value)));
                Text(dc, value.ToString("0"), 3, y(value) - 7);
            }
            foreach (long t in ChartTime.Ticks(From, To, width)) {
                dc.DrawLine(grid, new Point(x(t), top), new Point(x(t), top + height + 5));
                var time = new FormattedText(ChartTime.Label(t, To - From), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 14,
                    TryFindResource("Text") as Brush ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(time, new Point(Math.Max(left, Math.Min(left + width - time.Width, x(t) - time.Width / 2)), top + height + 10));
            }
            if (valid.Count == 0) { Text(dc, Words.T("この期間にデータはありません", "No samples in this window"), left + 18, top + height / 2 - 8); DrawSelection(dc, left, top, width, height, min, max); return; }
            var pen = FrozenPen(accent, 2);
            var rangeBrush = accent.Clone(); rangeBrush.Opacity = 0.22;
            var range = FrozenPen(rangeBrush, Math.Max(2, width / Math.Max(1, valid.Count)));
            // The caller supplies the same sample average used by the summary.
            // History bucket averages cannot be averaged again: bucket counts differ.
            bool showAverage = Average.HasValue && Average.Value >= min && Average.Value <= max;
            double averageY = showAverage ? y(Average.Value) : 0;
            if (showAverage) dc.DrawLine(FrozenPen(ink, 1, DashStyles.Dash), new Point(left, averageY), new Point(left + width, averageY));
            ChartPoint previous = null;
            foreach (var p in valid) {
                dc.DrawLine(range, new Point(x(p.UnixMs), y(p.Min)), new Point(x(p.UnixMs), y(p.Max)));
                if (previous != null && p.UnixMs - previous.UnixMs <= GapMs) dc.DrawLine(pen, new Point(x(previous.UnixMs), y(previous.Bpm)), new Point(x(p.UnixMs), y(p.Bpm)));
                else dc.DrawEllipse(accent, null, new Point(x(p.UnixMs), y(p.Bpm)), 2.5, 2.5);
                previous = p;
            }

            if (showAverage) {
                var label = new FormattedText(Words.T($"平均 {Average.Value:0.0} bpm", $"Avg {Average.Value:0.0} bpm"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Yu Gothic UI"), 12, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double boxX = Math.Max(left, left + width - label.Width - 8);
                double boxY = Math.Max(top, averageY - label.Height - 8);
                dc.DrawRoundedRectangle(TryFindResource("Surface") as Brush ?? Brushes.White, null, new Rect(boxX, boxY, label.Width + 8, label.Height + 4), 3, 3);
                dc.DrawText(label, new Point(boxX + 4, boxY + 2));
            }

            // Snap to the nearest recorded/bucketed point and show its real timestamp and value.
            // This makes the aggregate history buckets and individual live samples behave alike.
            DrawSelection(dc, left, top, width, height, min, max);
            if (!selecting && SelectedRange == null && pointerInside && pointer.X >= 0 && pointer.X <= ActualWidth && pointer.Y >= 0 && pointer.Y <= ActualHeight) {
                double pointerX = Math.Max(left, Math.Min(left + width, pointer.X));
                long hoveredTime = From + (long)((pointerX - left) / width * (To - From));
                ChartPoint selected = null;
                long nearestDistance = long.MaxValue;
                foreach (var point in valid) {
                    long distance = Math.Abs(point.UnixMs - hoveredTime);
                    if (distance < nearestDistance) { selected = point; nearestDistance = distance; }
                }
                if (selected != null) {
                    double selectedX = x(selected.UnixMs), selectedY = y(selected.Bpm);
                    var crosshair = new Pen(accent.Clone(), 1) { DashStyle = DashStyles.Dash };
                    crosshair.Brush.Opacity = 0.8;
                    crosshair.Freeze();
                    dc.DrawLine(crosshair, new Point(selectedX, top), new Point(selectedX, top + height));
                    dc.DrawEllipse(TryFindResource("Surface") as Brush ?? Brushes.White, pen, new Point(selectedX, selectedY), 5, 5);

                    var hovered = DateTimeOffset.FromUnixTimeMilliseconds(selected.UnixMs).LocalDateTime;
                    string stamp = hovered.ToString("HH:mm:ss.fff");
                    bool isSample = selected.Min == selected.Max && selected.Bpm == selected.Min;
                    string values = isSample
                        ? (Words.Language == "en" ? $"{stamp}\n{hovered:MM/dd} · Heart rate {selected.Bpm:0} bpm" : $"{stamp}\n{hovered:MM/dd} · 心拍数 {selected.Bpm:0} bpm")
                        : Words.Language == "en"
                            ? $"{stamp}\n{hovered:MM/dd} · Avg {selected.Bpm:0.0} bpm   Min {selected.Min:0}   Max {selected.Max:0}"
                            : $"{stamp}\n{hovered:MM/dd} · 平均 {selected.Bpm:0.0} bpm   最小 {selected.Min:0}   最大 {selected.Max:0}";
                    var label = new FormattedText(values, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Yu Gothic UI"), 12, TryFindResource("Text") as Brush ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    label.SetFontSize(17, 0, stamp.Length); label.SetFontWeight(FontWeights.SemiBold, 0, stamp.Length);
                    var padding = 10.0;
                    double boxWidth = label.Width + padding * 2, boxHeight = label.Height + padding * 2;
                    double boxX = selectedX + 14;
                    if (boxX + boxWidth > left + width) boxX = selectedX - boxWidth - 14;
                    boxX = Math.Max(left, Math.Min(left + width - boxWidth, boxX));
                    double boxY = Math.Max(top, Math.Min(top + height - boxHeight, selectedY - boxHeight - 12));
                    var box = new Rect(boxX, boxY, boxWidth, boxHeight);
                    dc.DrawRoundedRectangle(TryFindResource("Surface") as Brush ?? Brushes.White, FrozenPen(TryFindResource("Line") as Brush ?? Brushes.Gray, 1), box, 6, 6);
                    dc.DrawText(label, new Point(boxX + padding, boxY + padding));
                }
            }
        }
    }
}
