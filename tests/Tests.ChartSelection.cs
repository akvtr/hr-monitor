using System;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestChartDragReturnsToOrigin()
    {
        RunOnSta(() => {
            var chart = new HeartChart { From = 1700000000000, To = 1700000600000, RangeSelectionEnabled = true };
            try {
                chart.Measure(new Size(800, 300)); chart.Arrange(new Rect(0, 0, 800, 300));
                const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(HeartChart).GetField("dragView", members).SetValue(chart, new TimeWindow(chart.From, chart.To));
                typeof(HeartChart).GetField("dragOrigin", members).SetValue(chart, new Point(200, 60));
                typeof(HeartChart).GetField("selecting", members).SetValue(chart, true);
                var update = typeof(HeartChart).GetMethod("UpdateSelection", members);
                update.Invoke(chart, new object[] { new Point(300, 60) });
                var first = chart.SelectedRange;
                Check(first != null && first.To > first.From, "chart drag creates a nonempty interval after crossing the drag threshold");
                chart.SetSelectionSummary(new HistorySummary { Count = 1, Min = 70, Average = 70, Max = 70 });
                int revision = chart.SelectionRevision;
                update.Invoke(chart, new object[] { new Point(200, 60) });
                Check(chart.SelectedRange == null && chart.SelectedSummary == null && chart.SelectionRevision > revision,
                    "returning a chart drag to its origin clears the old interval and invalidates its analysis");
                var debounce = (DispatcherTimer)typeof(HeartChart).GetField("analysisDebounce", members).GetValue(chart);
                Check(!debounce.IsEnabled && (bool)typeof(HeartChart).GetField("selecting", members).GetValue(chart),
                    "zero-width chart selection stops analysis without ending the drag gesture");
                Check(AutomationProperties.GetHelpText(chart).Contains(Words.T("Ctrl＋ホイール", "Ctrl+wheel")),
                    "cleared chart selection restores accessible interaction instructions instead of stale statistics");
                chart.SetSelectionSummary(new HistorySummary { Count = 1, Average = 99 });
                Check(chart.SelectedSummary == null, "analysis completing after the interval disappears cannot restore stale statistics");
                update.Invoke(chart, new object[] { new Point(100, 60) });
                Check(chart.SelectedRange != null && chart.SelectedRange.To == first.From && chart.SelectedRange.From < first.From && debounce.IsEnabled,
                    "the same chart gesture can continue across the origin into a reverse interval");
                chart.ClearSelection();
                Check(chart.SelectedRange == null && !debounce.IsEnabled && !(bool)typeof(HeartChart).GetField("selecting", members).GetValue(chart),
                    "explicit chart selection clearing still ends the gesture and stops pending analysis");
            } finally { chart.ClearSelection(); }
        }, "Chart zero-width drag verification failed");
    }
}
