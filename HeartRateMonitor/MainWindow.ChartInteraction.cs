using System;
using System.Threading.Tasks;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private void ConfigureChartInteraction(HeartChart chart, bool isHistory)
        {
            chart.RangeSelectionEnabled = true; chart.ClearSelection();
            chart.AnalysisRequested += async range => {
                int revision = chart.SelectionRevision;
                try {
                    if (!isHistory) { chart.SetSelectionSummary(ChartAnalysis.SummarizeRaw(livePoints, range)); return; }
                    var summary = await Task.Run(() => history.Summarize(range));
                    if (!closing && revision == chart.SelectionRevision && (chart == liveChart || chart == historyChart)) chart.SetSelectionSummary(summary);
                } catch (Exception ex) {
                    if (!closing && revision == chart.SelectionRevision && (chart == liveChart || chart == historyChart)) chart.SetSelectionSummary(null, ex.Message);
                }
            };
            chart.ZoomRequested += (anchor, delta) => {
                if (isHistory) {
                    ZoomHistory(anchor, delta);
                } else {
                    liveChart.ClearSelection();
                    var domain = new TimeWindow(0, 3600000);
                    var zoom = HistorySelection.Zoom(domain, new TimeWindow(3600000 - liveWidth, 3600000), delta, 1);
                    liveWidth = zoom.To - zoom.From;
                    Tick();
                }
            };
        }
        private void ZoomHistory(double anchor, int delta)
        {
            if (historyData == null || historyNavigator.IsInteracting) return;
            var current = CurrentHistory();
            var zoom = HistorySelection.Zoom(HistoryDomain(), current, delta, anchor);
            if (zoom.From == current.From && zoom.To == current.To) return;
            historyWindow = zoom; historyWidth = zoom.To - zoom.From; ScheduleHistory();
        }
    }
}
