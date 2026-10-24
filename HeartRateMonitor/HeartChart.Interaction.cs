using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HeartRateMonitor
{
    public sealed partial class HeartChart
    {
        public bool RangeSelectionEnabled;
        public TimeWindow SelectedRange { get; private set; }
        public HistorySummary SelectedSummary { get; private set; }
        public int SelectionRevision { get; private set; }
        public event Action<double, int> ZoomRequested;
        public event Action<TimeWindow> AnalysisRequested;
        private readonly DispatcherTimer analysisDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        private TimeWindow dragView;
        private Point dragOrigin;
        private bool selecting, selectionMoved;
        private string selectionError;
        private double Fraction(double x) => Math.Max(0, Math.Min(1, (x - 42) / Math.Max(1, ActualWidth - 60)));
        private void InitializeInteraction()
        {
            Focusable = true;
            analysisDebounce.Tick += (s, e) => RequestAnalysis();
            Unloaded += (s, e) => analysisDebounce.Stop();
            Loaded += (s, e) => { if (SelectedRange != null && SelectedSummary == null) RequestAnalysis(); };
            PreviewMouseWheel += (s, e) => {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || ZoomRequested == null) return;
                ClearSelection(); ZoomRequested(Fraction(e.GetPosition(this).X), e.Delta); e.Handled = true;
            };
            MouseLeftButtonDown += (s, e) => {
                var point = e.GetPosition(this);
                if (!RangeSelectionEnabled || To <= From || point.X < 42 || point.X > ActualWidth - 18 || point.Y < 18 || point.Y > ActualHeight - 52) return;
                Focus(); selecting = true; selectionMoved = false; dragOrigin = point; dragView = new TimeWindow(From, To);
                CaptureMouse(); e.Handled = true;
            };
            MouseMove += (s, e) => { if (selecting) UpdateSelection(e.GetPosition(this)); };
            MouseLeftButtonUp += (s, e) => { if (selecting) { UpdateSelection(e.GetPosition(this)); FinishSelection(); e.Handled = true; } };
            LostMouseCapture += (s, e) => { if (selecting) FinishSelection(); };
            MouseRightButtonDown += (s, e) => { if (SelectedRange != null || selecting) { ClearSelection(); e.Handled = true; } };
            KeyDown += (s, e) => { if (e.Key == Key.Escape && (SelectedRange != null || selecting)) { ClearSelection(); e.Handled = true; } };
        }
        private void UpdateSelection(Point point)
        {
            if (!selectionMoved && Math.Abs(point.X - dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            selectionMoved = true;
            var range = HistorySelection.SelectedInterval(dragView, Fraction(dragOrigin.X), Fraction(point.X));
            if (range == null) return;
            if (SelectedRange != null && SelectedRange.From == range.From && SelectedRange.To == range.To) return;
            SelectedRange = range; SelectedSummary = null; selectionError = null; SelectionRevision++;
            analysisDebounce.Stop(); analysisDebounce.Start(); InvalidateVisual();
        }
        private void FinishSelection()
        {
            selecting = false; ReleaseMouseCapture();
            if (!selectionMoved) ClearSelection(); else RequestAnalysis();
        }
        private void RequestAnalysis()
        {
            analysisDebounce.Stop();
            if (SelectedRange == null) return;
            SelectionRevision++; AnalysisRequested?.Invoke(SelectedRange);
        }
        public void ClearSelection()
        {
            selecting = false; ReleaseMouseCapture(); analysisDebounce.Stop();
            SelectionRevision++; SelectedRange = null; SelectedSummary = null; selectionError = null;
            AutomationProperties.SetHelpText(this, Words.T("Ctrl＋ホイールで拡大・縮小。ドラッグで区間を選択。Esc・右クリックで選択解除。", "Ctrl+wheel to zoom. Drag to select an interval. Escape or right-click clears it."));
            InvalidateVisual();
        }
        public void SetSelectionSummary(HistorySummary summary, string error = null)
        {
            if (SelectedRange == null) return;
            SelectedSummary = summary; selectionError = error;
            AutomationProperties.SetHelpText(this, SelectionText()); InvalidateVisual();
        }
        private string SelectionText()
        {
            var range = SelectedRange;
            if (range == null) return "";
            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(range.From).LocalDateTime;
            var endTime = DateTimeOffset.FromUnixTimeMilliseconds(range.To).LocalDateTime;
            string format = range.To - range.From < 60000 ? "HH:mm:ss.fff" : "HH:mm:ss";
            string title = startTime.ToString(format) + "  —  " + endTime.ToString(format);
            string date = startTime.Date == endTime.Date ? startTime.ToString("MM/dd") : startTime.ToString("MM/dd") + " → " + endTime.ToString("MM/dd");
            var summary = SelectedSummary;
            string stats = selectionError != null ? Words.T("読込エラー: ", "Read failed: ") + selectionError
                : summary == null ? Words.T("平均を計算中…", "Calculating average…")
                : summary.Count == 0 ? Words.T("この区間に有効な心拍データはありません", "No valid heart-rate samples in this interval")
                : Words.T($"平均 {summary.Average:0.0} bpm · 最小 {summary.Min} · 最大 {summary.Max} · {summary.Count:N0}件", $"Avg {summary.Average:0.0} bpm · Min {summary.Min} · Max {summary.Max} · {summary.Count:N0} samples");
            return title + "\n" + date + " · " + ChartTime.Duration(range.To - range.From) + "\n" + stats + "\n" + Words.T("Esc・右クリックで解除", "Escape / right-click to clear");
        }
        private void DrawSelection(DrawingContext dc, double left, double top, double width, double height, double min, double max)
        {
            if (SelectedRange == null) return;
            double a = left + Math.Max(0, Math.Min(1, (SelectedRange.From - From) / (double)Math.Max(1, To - From))) * width;
            double b = left + Math.Max(0, Math.Min(1, (SelectedRange.To - From) / (double)Math.Max(1, To - From))) * width;
            var tint = accent.CloneCurrentValue(); tint.Opacity = 0.14; tint.Freeze();
            dc.DrawRectangle(tint, null, new Rect(a, top, Math.Max(1, b - a), height));
            var edge = FrozenPen(accent, 1);
            dc.DrawLine(edge, new Point(a, top), new Point(a, top + height)); dc.DrawLine(edge, new Point(b, top), new Point(b, top + height));
            if (SelectedSummary != null && SelectedSummary.Count > 0 && SelectedSummary.Average >= min && SelectedSummary.Average <= max) {
                double y = top + (max - SelectedSummary.Average) / (max - min) * height;
                dc.DrawLine(FrozenPen(accent, 1.5, DashStyles.Dash), new Point(a, y), new Point(b, y));
            }
            var label = new FormattedText(SelectionText(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI, Yu Gothic UI"), 12,
                TryFindResource("Text") as Brush ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(40, Math.Min(520, width - 24)) };
            int headline = SelectionText().IndexOf('\n');
            if (headline > 0) { label.SetFontSize(18, 0, headline); label.SetFontWeight(FontWeights.SemiBold, 0, headline); }
            double boxWidth = Math.Min(width - 12, label.Width + 20), boxX = Math.Max(left + 6, Math.Min(left + width - boxWidth - 6, a + 8));
            dc.DrawRoundedRectangle(TryFindResource("SurfaceRaised") as Brush ?? Brushes.White, FrozenPen(accent, 1), new Rect(boxX, top + 6, boxWidth, label.Height + 16), 5, 5);
            dc.DrawText(label, new Point(boxX + 10, top + 14));
            Func<long, FormattedText> timeTag = value => new FormattedText(DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("HH:mm:ss"),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Bahnschrift, Segoe UI"), 13, accent, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var startTag = timeTag(SelectedRange.From); var endTag = timeTag(SelectedRange.To);
            if (b - a >= startTag.Width + endTag.Width + 16) {
                dc.DrawText(startTag, new Point(a, top + height + 34)); dc.DrawText(endTag, new Point(b - endTag.Width, top + height + 34));
            } else {
                var pair = new FormattedText(DateTimeOffset.FromUnixTimeMilliseconds(SelectedRange.From).LocalDateTime.ToString("HH:mm:ss") + " — " +
                    DateTimeOffset.FromUnixTimeMilliseconds(SelectedRange.To).LocalDateTime.ToString("HH:mm:ss"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Bahnschrift, Segoe UI"), 13, accent, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(pair, new Point(Math.Max(left, Math.Min(left + width - pair.Width, (a + b - pair.Width) / 2)), top + height + 34));
            }
        }
    }
}
