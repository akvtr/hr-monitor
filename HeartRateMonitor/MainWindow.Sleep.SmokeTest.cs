using System;
using System.Linq;
using System.Threading.Tasks;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private async Task VerifySleepLabels()
        {
            var range = historyWindow;
            using (var store = new Store(database)) {
                if (store.SleepIntervals(range.From, range.To).Count == 0) {
                    var tracker = new SleepIntervalTracker(store);
                    // Isolated --smoke-test data only. Do not manufacture heart-rate samples.
                    long start = range.From + (range.To - range.From) / 3;
                    for (int i = 0; i < 18; i++) tracker.Observe(true, start + i * 5000);
                    tracker.Observe(false, start + 90000);
                }
            }
            await LoadHistory();
            await Task.Delay(120); UpdateLayout();
            if (!historyChart.SleepIntervals.Any()) throw new InvalidOperationException("Automatic sleep intervals did not reach the history chart.");
            if (GetType().GetMethod("CreateAnnotationDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null)
                throw new InvalidOperationException("Manual annotation controls must be removed.");
        }
    }
}
