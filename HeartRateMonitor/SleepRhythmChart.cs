using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed class SleepRhythmChart : FrameworkElement
    {
        public List<SleepInterval> Intervals = new List<SleepInterval>();
        public DateTime FirstDay = DateTime.Today.AddDays(-6);
        public DateTime LastDay = DateTime.Today;
        public int BoundaryHour;
        public TimeZoneInfo Zone = TimeZoneInfo.Local;
        public long? SelectedId;
        public event Action<long> IntervalSelected;
        private const double RowHeight = 26, Left = 94, Right = 20;
        private readonly List<Segment> segments = new List<Segment>();

        private sealed class Segment
        {
            internal Rect Bounds;
            internal SleepInterval Interval;
        }

        public SleepRhythmChart()
        {
            ClipToBounds = true; Focusable = true; MinHeight = 144;
            AutomationProperties.SetAutomationId(this, "SleepRhythmChart");
            AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
            UpdateAccessibleSelection();
            AutomationProperties.SetHelpText(this, Words.T("睡眠区間をクリック、または矢印キーで選択。Homeで最初、Endで最後の区間へ移動します。", "Click a sleep interval or use arrow keys to select one. Home selects the first interval; End selects the last."));
            GotKeyboardFocus += (s, e) => InvalidateVisual();
            LostKeyboardFocus += (s, e) => InvalidateVisual();
            KeyDown += (s, e) => {
                if (e.Key != Key.Left && e.Key != Key.Up && e.Key != Key.Right && e.Key != Key.Down &&
                    e.Key != Key.Home && e.Key != Key.End && e.Key != Key.Enter && e.Key != Key.Space) return;
                var records = VisibleIntervals();
                if (records.Count == 0) return;
                int current = records.FindIndex(interval => interval.Id == SelectedId);
                int index = current;
                if (e.Key == Key.Home) index = 0;
                else if (e.Key == Key.End) index = records.Count - 1;
                else if (e.Key == Key.Left || e.Key == Key.Up) index = current < 0 ? records.Count - 1 : Math.Max(0, current - 1);
                else if (e.Key == Key.Right || e.Key == Key.Down) index = current < 0 ? 0 : Math.Min(records.Count - 1, current + 1);
                else if (index < 0) index = 0;
                SelectInterval(records[index].Id); e.Handled = true;
            };
            MouseMove += (s, e) => {
                var segment = Hit(e.GetPosition(this));
                Cursor = segment == null ? Cursors.Arrow : Cursors.Hand;
                ToolTip = segment == null ? null : Description(segment.Interval);
            };
            MouseLeave += (s, e) => { ToolTip = null; Cursor = Cursors.Arrow; };
            MouseLeftButtonDown += (s, e) => {
                var segment = Hit(e.GetPosition(this));
                if (segment == null) return;
                Focus(); SelectInterval(segment.Interval.Id); e.Handled = true;
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
            ToolTip = null; UpdateAccessibleSelection(); InvalidateMeasure(); InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = Math.Max(1, Math.Min(7, (LastDay.Date - FirstDay.Date).Days + 1));
            double plotWidth = (double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width) - Left - Right;
            double desiredHeight = HeaderHeight(plotWidth) + RowHeight * count + 4;
            return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? desiredHeight : Math.Min(desiredHeight, availableSize.Height));
        }

        private List<SleepInterval> VisibleIntervals()
        {
            long from = Store.SleepDayBoundary(FirstDay, Zone, BoundaryHour), to = Store.SleepDayBoundary(LastDay.AddDays(1), Zone, BoundaryHour);
            return Intervals.Where(interval => interval != null && interval.To > interval.From && interval.From < to && interval.To > from)
                .OrderBy(interval => interval.From).ThenBy(interval => interval.Id).ToList();
        }

        public void SelectInterval(long id)
        {
            if (!VisibleIntervals().Any(interval => interval.Id == id)) return;
            SelectedId = id; UpdateAccessibleSelection(); InvalidateVisual(); IntervalSelected?.Invoke(id);
            UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

        private void UpdateAccessibleSelection()
        {
            var interval = Intervals.FirstOrDefault(item => item != null && item.Id == SelectedId);
            AutomationProperties.SetName(this, Words.T("睡眠リズム", "Sleep rhythm") + (interval == null ? "" : " · " + Description(interval)));
        }

        private double DayHeight(DateTime day)
        {
            long duration = Store.SleepDayBoundary(day.AddDays(1), Zone, BoundaryHour) - Store.SleepDayBoundary(day, Zone, BoundaryHour);
            return duration == 86400000 ? RowHeight : RowHeight + 20;
        }

        internal double MidnightPosition(DateTime day)
        {
            long from = Store.SleepDayBoundary(day, Zone, BoundaryHour), to = Store.SleepDayBoundary(day.AddDays(1), Zone, BoundaryHour);
            long midnight = Store.SleepDayBoundary(day.AddDays(1), Zone, 0);
            return Math.Max(0, Math.Min(1, (midnight - from) / (double)Math.Max(1, to - from)));
        }

        private FormattedText MidnightLabel(Brush brush, double size)
        {
            return new FormattedText(Words.T("00:00 翌日", "00:00 +1d"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Yu Gothic UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        private bool MidnightOverlapsEndpoint(double labelX, double labelWidth, double plotWidth)
        {
            return BoundaryHour != 0 && (labelX < Left + 22 || labelX + labelWidth > Left + plotWidth - 18);
        }

        private double HeaderHeight(double plotWidth)
        {
            var label = MidnightLabel(Brushes.Gray, 11);
            double x = Left + ((24 - BoundaryHour) / 24.0) * plotWidth;
            double labelX = Math.Max(Left, Math.Min(Left + plotWidth - label.Width, x - label.Width / 2));
            return MidnightOverlapsEndpoint(labelX, label.Width, plotWidth) ? 38 : 24;
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
            var midnightPen = new Pen(Brush("TextMuted", ink), 1.3) { DashStyle = DashStyles.Dash };
            if (IsKeyboardFocusWithin && ActualWidth > 2 && ActualHeight > 2)
                dc.DrawRectangle(null, new Pen(Brush("Focus", accent), 1), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2));
            // Rows use actual elapsed time between local day boundaries. Their hour ticks
            // are converted separately, keeping daylight-saving days truthful as well.
            int step = width < 400 ? 6 : 4;
            var midnightLabel = MidnightLabel(ink, 11);
            double midnightHeaderX = Left + ((24 - BoundaryHour) / 24.0) * width;
            double midnightLabelX = Math.Max(Left, Math.Min(Left + width - midnightLabel.Width, midnightHeaderX - midnightLabel.Width / 2));
            double headerHeight = HeaderHeight(width);
            bool secondHeaderLine = headerHeight > 24;
            for (int hour = 0; hour <= 24; hour += step) {
                double x = Left + hour / 24.0 * width;
                dc.DrawLine(grid, new Point(x, headerHeight - 3), new Point(x, headerHeight));
                double labelX = Math.Max(Left - 14, Math.Min(ActualWidth - 34, x - 14));
                if (secondHeaderLine || labelX + 32 < midnightLabelX - 4 || labelX > midnightLabelX + midnightLabel.Width + 4)
                    Text(dc, ((BoundaryHour + hour) % 24).ToString("00") + ":00", labelX, 5, ink);
            }
            dc.DrawText(midnightLabel, new Point(midnightLabelX, secondHeaderLine ? 20 : 5));
            bool visible = false;
            double y = headerHeight;
            double naturalHeight = 0;
            for (int i = 0; i < count; i++) naturalHeight += DayHeight(FirstDay.Date.AddDays(i));
            double scale = Math.Max(0, ActualHeight - headerHeight - 4) / naturalHeight;
            for (int i = 0; i < count; i++) {
                var day = FirstDay.Date.AddDays(i);
                long from = Store.SleepDayBoundary(day, Zone, BoundaryHour), to = Store.SleepDayBoundary(day.AddDays(1), Zone, BoundaryHour);
                bool shifted = to - from != 86400000;
                double midnightX = Left + MidnightPosition(day) * width;
                var shiftedMidnightLabel = shifted ? MidnightLabel(ink, 9) : null;
                double shiftedLabelX = shifted ? Math.Max(Left, Math.Min(Left + width - shiftedMidnightLabel.Width, midnightX - shiftedMidnightLabel.Width / 2)) : 0;
                bool showShiftedMidnightLabel = shifted && !MidnightOverlapsEndpoint(shiftedLabelX, shiftedMidnightLabel.Width, width);
                double rowHeight = (shifted ? RowHeight + 20 : RowHeight) * scale;
                double labelHeight = shifted ? Math.Min(16, rowHeight * 0.45) : 0;
                double barHeight = Math.Max(4, Math.Min(14, rowHeight - labelHeight - 4));
                double barY = y + labelHeight + (rowHeight - labelHeight - barHeight) / 2;
                Text(dc, day.ToString(Words.Language == "en" ? "MM/dd ddd" : "MM/dd (ddd)", Words.Language == "en" ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("ja-JP")), 0, barY - 1, ink, rowHeight < 20 ? 10 : 11);
                if (shifted) Text(dc, Words.T($"時刻変更 · {(to - from) / 3600000.0:0.#}時間", $"Clock change · {(to - from) / 3600000.0:0.#}h"), 0, y + 1, ink, 9);
                dc.DrawLine(grid, new Point(Left, y + rowHeight), new Point(Left + width, y + rowHeight));
                if (to <= from) { y += rowHeight; continue; }
                for (int hour = 0; hour <= 24; hour += step) {
                    var tickDay = day.AddDays((BoundaryHour + hour) / 24);
                    long tick = Store.SleepDayBoundary(tickDay, Zone, (BoundaryHour + hour) % 24);
                    double x = Left + Math.Max(0, Math.Min(1, (tick - from) / (double)(to - from))) * width;
                    dc.DrawLine(grid, new Point(x, y + labelHeight), new Point(x, y + rowHeight));
                    if (shifted) {
                        string label = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(tick), Zone).ToString("HH:mm");
                        double labelX = Math.Max(Left - 14, Math.Min(ActualWidth - 34, x - 14));
                        if (!showShiftedMidnightLabel || labelX + 28 < shiftedLabelX - 4 || labelX > shiftedLabelX + shiftedMidnightLabel.Width + 4)
                            Text(dc, label, labelX, y + 1, ink, 9);
                    }
                }
                foreach (var interval in Intervals) {
                    if (interval == null || interval.To <= interval.From || interval.From >= to || interval.To <= from) continue;
                    double x1 = Left + (Math.Max(from, interval.From) - from) / (double)(to - from) * width;
                    double x2 = Left + (Math.Min(to, interval.To) - from) / (double)(to - from) * width;
                    var rect = new Rect(x1, barY, Math.Max(1, x2 - x1), barHeight);
                    var fill = interval.Excluded ? Brush("SurfaceAlt", Brushes.LightGray) : accent;
                    var outline = new Pen(interval.Id == SelectedId ? Brush("Focus", accent) : interval.Excluded ? ink : accent,
                        interval.Id == SelectedId ? 2 : 1);
                    if (interval.Excluded || interval.IsOngoing) outline.DashStyle = DashStyles.Dash;
                    dc.DrawRectangle(fill, outline, rect);
                    if (interval.IsOngoing) dc.DrawLine(new Pen(Brush("Text", Brushes.Black), 2), new Point(rect.Right, rect.Top + 2), new Point(rect.Right, rect.Bottom - 2));
                    double hitLeft = Math.Max(Left, rect.Left - 3), hitRight = Math.Min(Left + width, rect.Right + 3);
                    var hit = new Rect(hitLeft, y + labelHeight, Math.Max(0, hitRight - hitLeft), Math.Max(0, rowHeight - labelHeight));
                    segments.Add(new Segment { Bounds = hit, Interval = interval }); visible = true;
                }
                dc.DrawLine(midnightPen, new Point(midnightX, y + labelHeight), new Point(midnightX, y + rowHeight));
                if (showShiftedMidnightLabel) dc.DrawText(shiftedMidnightLabel, new Point(shiftedLabelX, y + 1));
                y += rowHeight;
            }
            if (!visible) Text(dc, Words.T("この期間に睡眠記録はありません", "No sleep records in this period"), Left + 12, headerHeight + 8, ink, 12);
        }
    }
}
