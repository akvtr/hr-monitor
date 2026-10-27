using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed class SleepRhythmChart : FrameworkElement
    {
        public List<SleepInterval> Intervals = new List<SleepInterval>();
        public DateTime FirstDay = DateTime.Today.AddDays(-6);
        public DateTime LastDay = DateTime.Today;
        public int BoundaryHour = 12;
        public TimeZoneInfo Zone = TimeZoneInfo.Local;
        public long? SelectedId;
        public event Action<long> IntervalSelected;
        private const double RowHeight = 26, HeaderHeight = 30, Left = 94, Right = 20;
        private readonly List<Segment> segments = new List<Segment>();

        private sealed class Segment
        {
            internal Rect Bounds;
            internal SleepInterval Interval;
        }

        public SleepRhythmChart()
        {
            ClipToBounds = true; Focusable = false; Height = HeaderHeight + RowHeight * 7 + 8;
            AutomationProperties.SetAutomationId(this, "SleepRhythmChart");
            AutomationProperties.SetName(this, Words.T("睡眠リズム。記録一覧からも各区間を選択できます。", "Sleep rhythm. Each interval can also be selected from the records list."));
            MouseMove += (s, e) => {
                var segment = Hit(e.GetPosition(this));
                Cursor = segment == null ? Cursors.Arrow : Cursors.Hand;
                ToolTip = segment == null ? null : Description(segment.Interval);
            };
            MouseLeave += (s, e) => { ToolTip = null; Cursor = Cursors.Arrow; };
            MouseLeftButtonDown += (s, e) => {
                var segment = Hit(e.GetPosition(this));
                if (segment == null) return;
                SelectedId = segment.Interval.Id; InvalidateVisual(); IntervalSelected?.Invoke(segment.Interval.Id); e.Handled = true;
            };
        }

        public void SetData(List<SleepInterval> intervals, DateTime firstDay, DateTime lastDay, int boundaryHour, TimeZoneInfo zone)
        {
            if (intervals == null) throw new ArgumentNullException(nameof(intervals));
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (boundaryHour < 0 || boundaryHour > 23) throw new ArgumentOutOfRangeException(nameof(boundaryHour));
            if (lastDay.Date < firstDay.Date || (lastDay.Date - firstDay.Date).Days > 365 || lastDay.Date == DateTime.MaxValue.Date)
                throw new ArgumentOutOfRangeException(nameof(lastDay));
            Intervals = intervals; FirstDay = firstDay.Date; LastDay = lastDay.Date; BoundaryHour = boundaryHour; Zone = zone;
            Refresh();
        }

        public void Refresh()
        {
            int count = Math.Max(1, Math.Min(366, (LastDay.Date - FirstDay.Date).Days + 1));
            double height = HeaderHeight + 8;
            for (int i = 0; i < count; i++) height += DayHeight(FirstDay.Date.AddDays(i));
            Height = height;
            ToolTip = null; InvalidateVisual();
        }

        private double DayHeight(DateTime day)
        {
            long duration = Store.SleepDayBoundary(day.AddDays(1), Zone, BoundaryHour) - Store.SleepDayBoundary(day, Zone, BoundaryHour);
            return duration == 86400000 ? RowHeight : RowHeight + 20;
        }

        private Segment Hit(Point point) => segments.LastOrDefault(segment => segment.Bounds.Contains(point));
        private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        private void Text(DrawingContext dc, string text, double x, double y, Brush brush, double size = 11)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Yu Gothic UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        }

        private string Description(SleepInterval interval)
        {
            var start = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(interval.From), Zone);
            var end = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(interval.To), Zone);
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, interval.To - interval.From));
            string state = interval.Excluded ? Words.T(" · 集計から除外", " · Excluded from averages") : "";
            if (interval.IsOngoing) state += Words.T(" · 記録中", " · Recording");
            string format = start.Offset == end.Offset ? "yyyy/MM/dd HH:mm" : "yyyy/MM/dd HH:mm zzz";
            return start.ToString(format) + " – " + end.ToString(format)
                + Words.T($" · {(int)duration.TotalHours}時間{duration.Minutes:00}分", $" · {(int)duration.TotalHours}h {duration.Minutes:00}m") + state;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); segments.Clear();
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(new Point(0, 0), RenderSize));
            double width = ActualWidth - Left - Right;
            int count = Math.Max(0, Math.Min(366, (LastDay.Date - FirstDay.Date).Days + 1));
            if (width < 40 || count == 0 || BoundaryHour < 0 || BoundaryHour > 23 || Zone == null) return;
            var ink = Brush("TextMuted", Brushes.Gray);
            var grid = new Pen(Brush("ChartGrid", Brushes.LightGray), 0.7);
            var accent = Brush("Accent", Brushes.Teal);
            // Rows use actual elapsed time between local day boundaries. Their hour ticks
            // are converted separately, keeping daylight-saving days truthful as well.
            int step = width < 400 ? 6 : 4;
            for (int hour = 0; hour <= 24; hour += step) {
                double x = Left + hour / 24.0 * width;
                dc.DrawLine(grid, new Point(x, HeaderHeight - 3), new Point(x, HeaderHeight));
                Text(dc, ((BoundaryHour + hour) % 24).ToString("00") + ":00", Math.Max(Left - 14, Math.Min(ActualWidth - 34, x - 14)), 5, ink);
            }
            bool visible = false;
            double y = HeaderHeight;
            for (int i = 0; i < count; i++) {
                var day = FirstDay.Date.AddDays(i);
                long from = Store.SleepDayBoundary(day, Zone, BoundaryHour), to = Store.SleepDayBoundary(day.AddDays(1), Zone, BoundaryHour);
                bool shifted = to - from != 86400000;
                double rowHeight = shifted ? RowHeight + 20 : RowHeight;
                double barY = y + (shifted ? 26 : 6);
                Text(dc, day.ToString(Words.Language == "en" ? "MM/dd ddd" : "MM/dd (ddd)", Words.Language == "en" ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ja-JP")), 0, barY - 1, ink);
                if (shifted) Text(dc, Words.T($"時刻変更 · {(to - from) / 3600000.0:0.#}時間", $"Clock change · {(to - from) / 3600000.0:0.#}h"), 0, y + 4, ink, 10);
                dc.DrawLine(grid, new Point(Left, y + rowHeight), new Point(Left + width, y + rowHeight));
                if (to <= from) { y += rowHeight; continue; }
                for (int hour = 0; hour <= 24; hour += step) {
                    var tickDay = day.AddDays((BoundaryHour + hour) / 24);
                    long tick = Store.SleepDayBoundary(tickDay, Zone, (BoundaryHour + hour) % 24);
                    double x = Left + Math.Max(0, Math.Min(1, (tick - from) / (double)(to - from))) * width;
                    dc.DrawLine(grid, new Point(x, shifted ? y + 20 : y), new Point(x, y + rowHeight));
                    if (shifted) {
                        string label = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(tick), Zone).ToString("HH:mm");
                        Text(dc, label, Math.Max(Left - 14, Math.Min(ActualWidth - 34, x - 14)), y + 4, ink, 10);
                    }
                }
                foreach (var interval in Intervals) {
                    if (interval == null || interval.To <= interval.From || interval.From >= to || interval.To <= from) continue;
                    double x1 = Left + (Math.Max(from, interval.From) - from) / (double)(to - from) * width;
                    double x2 = Left + (Math.Min(to, interval.To) - from) / (double)(to - from) * width;
                    var rect = new Rect(x1, barY, Math.Max(1, x2 - x1), 14);
                    var fill = interval.Excluded ? Brush("SurfaceAlt", Brushes.LightGray) : accent;
                    var outline = new Pen(interval.Id == SelectedId ? Brush("Focus", accent) : interval.Excluded ? ink : accent,
                        interval.Id == SelectedId ? 2 : 1);
                    if (interval.Excluded || interval.IsOngoing) outline.DashStyle = DashStyles.Dash;
                    dc.DrawRectangle(fill, outline, rect);
                    if (interval.IsOngoing) dc.DrawLine(new Pen(Brush("Text", Brushes.Black), 2), new Point(rect.Right, rect.Top + 2), new Point(rect.Right, rect.Bottom - 2));
                    var hit = new Rect(Math.Max(Left, rect.Left - 3), barY - 4, Math.Min(width, rect.Width + 6), RowHeight - 3);
                    segments.Add(new Segment { Bounds = hit, Interval = interval }); visible = true;
                }
                y += rowHeight;
            }
            if (!visible) Text(dc, Words.T("この期間に睡眠記録はありません", "No sleep records in this period"), Left + 12, HeaderHeight + 8, ink, 12);
        }
    }
}
