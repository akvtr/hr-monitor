using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HeartRateMonitor
{
    public sealed class HistoryNavigator : Grid
    {
        private readonly Overview plot;
        private readonly Canvas overlay = new Canvas();
        private readonly Thumb left, right, center;
        private TimeWindow domain, selection, dragStart;
        private double dragDistance;
        public bool IsInteracting { get; private set; }
        public TimeWindow Selection => selection;
        public event Action<TimeWindow> SelectionChanged;
        public event Action InteractionStarted;
        public event Action InteractionCompleted;
        public event Action<double, int> ZoomRequested;
        private double PlotWidth => Math.Max(1, ActualWidth - 60);
        public HistoryNavigator()
        {
            Height = 84; ClipToBounds = true;
            plot = new Overview(this); Children.Add(plot); Children.Add(overlay);
            center = MakeThumb("HistorySelection", Words.T("選択期間を移動", "Move selected time range"), "HistoryRangeSelection", Cursors.SizeAll, 0);
            left = MakeThumb("HistoryRangeStart", Words.T("表示期間の開始", "Start of visible range"), "HistoryRangeHandle", Cursors.SizeWE, -1);
            right = MakeThumb("HistoryRangeEnd", Words.T("表示期間の終了", "End of visible range"), "HistoryRangeHandle", Cursors.SizeWE, 1);
            SizeChanged += (s, e) => UpdateVisuals();
            PreviewMouseWheel += (s, e) => {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || ZoomRequested == null || domain == null || selection == null || !IsEnabled) return;
                e.Handled = true; if (IsInteracting) return;
                double time = domain.From + (e.GetPosition(this).X - 42) / PlotWidth * (domain.To - domain.From);
                ZoomRequested(Math.Max(0, Math.Min(1, (time - selection.From) / (selection.To - selection.From))), e.Delta);
            };
            MouseLeftButtonDown += (s, e) => {
                if (domain == null || selection == null || IsInteracting || e.Handled) return;
                double x = e.GetPosition(this).X;
                if (x < 42 || x > ActualWidth - 18) return;
                long time = domain.From + (long)((x - 42) / PlotWidth * (domain.To - domain.From));
                Publish(HistorySelection.Move(domain, selection, time - (selection.From + (selection.To - selection.From) / 2)));
                InteractionCompleted?.Invoke(); e.Handled = true;
            };
            AutomationProperties.SetAutomationId(this, "HistoryNavigator");
            AutomationProperties.SetName(this, Words.T("全体グラフと表示範囲", "Overview chart and visible range"));
        }
        private Thumb MakeThumb(string id, string name, string style, Cursor cursor, int edge)
        {
            var thumb = new Thumb { Height = edge == 0 ? 50 : 28, Width = edge == 0 ? 1 : 12, Cursor = cursor, Focusable = true };
            thumb.SetResourceReference(StyleProperty, style);
            AutomationProperties.SetAutomationId(thumb, id); AutomationProperties.SetName(thumb, name);
            overlay.Children.Add(thumb);
            thumb.DragStarted += (s, e) => { IsInteracting = true; dragStart = selection; dragDistance = 0; InteractionStarted?.Invoke(); };
            thumb.DragDelta += (s, e) => {
                if (domain == null || dragStart == null) return;
                dragDistance += e.HorizontalChange;
                long delta = (long)(dragDistance / PlotWidth * (domain.To - domain.From));
                Publish(edge == 0 ? HistorySelection.Move(domain, dragStart, delta)
                    : HistorySelection.Resize(domain, dragStart, edge < 0, (edge < 0 ? dragStart.From : dragStart.To) + delta));
            };
            thumb.DragCompleted += (s, e) => { IsInteracting = false; dragStart = null; InteractionCompleted?.Invoke(); };
            thumb.KeyDown += (s, e) => {
                if (domain == null || selection == null) return;
                long delta = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 600000 : 60000;
                if (e.Key == Key.Left || e.Key == Key.Right) {
                    if (e.Key == Key.Left) delta = -delta;
                    Publish(edge == 0 ? HistorySelection.Move(domain, selection, delta)
                        : HistorySelection.Resize(domain, selection, edge < 0, (edge < 0 ? selection.From : selection.To) + delta));
                } else if (e.Key == Key.Home || e.Key == Key.End) {
                    long boundary = e.Key == Key.Home ? domain.From : domain.To;
                    Publish(edge == 0 ? HistorySelection.Move(domain, selection, boundary - (e.Key == Key.Home ? selection.From : selection.To))
                        : HistorySelection.Resize(domain, selection, edge < 0, boundary));
                } else return;
                e.Handled = true; InteractionCompleted?.Invoke();
            };
            return thumb;
        }
        public void SetData(TimeWindow bounds, List<ChartPoint> points, TimeWindow visible, bool enabled)
        {
            domain = bounds; plot.Points = points; IsEnabled = enabled;
            SetSelection(visible);
        }
        public void SetSelection(TimeWindow visible)
        {
            selection = domain == null || visible == null ? null : HistorySelection.Clamp(domain, visible);
            UpdateVisuals();
        }
        private void Publish(TimeWindow value)
        {
            if (selection != null && value.From == selection.From && value.To == selection.To) return;
            selection = value; UpdateVisuals(); SelectionChanged?.Invoke(value);
        }
        private void UpdateVisuals()
        {
            bool ready = domain != null && selection != null;
            overlay.Visibility = ready && IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (ready) {
                double x1 = 42 + (selection.From - domain.From) * PlotWidth / (domain.To - domain.From);
                double x2 = 42 + (selection.To - domain.From) * PlotWidth / (domain.To - domain.From);
                // Keep a usable move target even when ten minutes occupies only a few pixels in a full day.
                center.Width = Math.Max(24, x2 - x1); Canvas.SetLeft(center, (x1 + x2 - center.Width) / 2); Canvas.SetTop(center, 6);
                Canvas.SetLeft(left, x1 - 6); Canvas.SetLeft(right, x2 - 6); Canvas.SetTop(left, 17); Canvas.SetTop(right, 17);
                AutomationProperties.SetHelpText(left, Format(selection.From)); AutomationProperties.SetHelpText(right, Format(selection.To));
                AutomationProperties.SetHelpText(center, Format(selection.From) + " — " + Format(selection.To));
            }
            plot.InvalidateVisual();
        }
        private static string Format(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("MM/dd HH:mm:ss");
        private sealed class Overview : FrameworkElement
        {
            private readonly HistoryNavigator owner;
            public List<ChartPoint> Points = new List<ChartPoint>();
            public Overview(HistoryNavigator owner) { this.owner = owner; }
            private Brush Brush(string key) => owner.TryFindResource(key) as Brush ?? Brushes.Gray;
            protected override void OnRender(DrawingContext dc)
            {
                double width = owner.PlotWidth, left = 42, top = 6, bottom = 56;
                var area = new Rect(left, top, width, bottom - top);
                dc.DrawRectangle(Brush("SurfaceAlt"), new Pen(Brush("Line"), 1), area);
                var bounds = owner.domain;
                if (bounds == null) return;
                Func<long, double> x = t => left + (t - bounds.From) * width / (bounds.To - bounds.From);
                double min = double.MaxValue, max = double.MinValue;
                foreach (var p in Points) { min = Math.Min(min, p.Min); max = Math.Max(max, p.Max); }
                if (Points.Count > 0) {
                    min = Math.Max(0, min - 8); max = Math.Max(min + 30, max + 8);
                    Func<double, double> y = v => bottom - 4 - (v - min) / (max - min) * (bottom - top - 8);
                    var pen = new Pen(Brush("Accent").CloneCurrentValue(), 1.2); pen.Freeze();
                    ChartPoint previous = null; long gap = Math.Max(15000, (bounds.To - bounds.From) / 1200 * 2);
                    dc.PushClip(new RectangleGeometry(area));
                    foreach (var p in Points) {
                        if (previous != null && p.UnixMs - previous.UnixMs <= gap) dc.DrawLine(pen, new Point(x(previous.UnixMs), y(previous.Bpm)), new Point(x(p.UnixMs), y(p.Bpm)));
                        else dc.DrawEllipse(pen.Brush, null, new Point(x(p.UnixMs), y(p.Bpm)), 1.5, 1.5);
                        previous = p;
                    }
                    dc.Pop();
                }
                if (owner.selection != null && owner.IsEnabled) {
                    double a = x(owner.selection.From), b = x(owner.selection.To);
                    var mask = Brush("Surface").CloneCurrentValue(); mask.Opacity = 0.68; mask.Freeze();
                    dc.DrawRectangle(mask, null, new Rect(left, top, Math.Max(0, a - left), bottom - top));
                    dc.DrawRectangle(mask, null, new Rect(b, top, Math.Max(0, left + width - b), bottom - top));
                    var fill = Brush("Accent").CloneCurrentValue(); fill.Opacity = 0.1; fill.Freeze();
                    dc.DrawRectangle(fill, new Pen(Brush("Accent"), 1), new Rect(a, top, Math.Max(1, b - a), bottom - top));
                }
                foreach (long time in ChartTime.Ticks(bounds.From, bounds.To, width)) {
                    var label = new FormattedText(DateTimeOffset.FromUnixTimeMilliseconds(time).LocalDateTime.ToString("HH:mm"), CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, Brush("Text"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(label, new Point(Math.Max(left, Math.Min(left + width - label.Width, x(time) - label.Width / 2)), 64));
                }
            }
        }
    }
}
