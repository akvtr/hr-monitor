using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private long smokeSleepId, smokeSleepEmptyId;

        private async Task VerifySleepManagement()
        {
            if (!smokeTest) throw new InvalidOperationException("Sleep-management fixtures require isolated smoke mode.");
            if (smokeSleepId == 0) {
                // Explicit --smoke-test only: these are labeled test readings, never normal data.
                long start = new DateTimeOffset(DateTime.Today.AddDays(-2).AddHours(1)).ToUnixTimeMilliseconds();
                using (var store = new Store(database)) {
                    var sleep = new SleepInterval { From = start, To = start + 6 * 3600000L };
                    store.SaveSleepInterval(sleep); smokeSleepId = sleep.Id;
                    var empty = new SleepInterval { From = start - 86400000L, To = start - 86400000L + 5 * 3600000L };
                    store.SaveSleepInterval(empty); smokeSleepEmptyId = empty.Id;
                    foreach (var reading in new[] {
                        new Reading { UnixMs = start + 60000, Bpm = 60 },
                        new Reading { UnixMs = start + 120000, Bpm = 80 },
                        new Reading { UnixMs = start + 180000, Bpm = 0 },
                        new Reading { UnixMs = start + 240000, Bpm = 200, Contact = false }
                    }) {
                        reading.Device = "ISOLATED SLEEP UI TEST"; reading.Session = "smoke-sleep"; store.Add(reading);
                    }
                }
            }
            await RefreshSleepManagement();
            var record = sleepAnalysis.Records.Single(item => item.Interval.Id == smokeSleepId);
            int recordCount = sleepAnalysis.Records.Count;
            if (record.Summary.Count != 2 || Math.Abs(record.Summary.Average - 70) > 0.001)
                throw new InvalidOperationException("Sleep details must average only valid measured heart rates.");
            if (string.IsNullOrWhiteSpace(sleepAverageHeart.Text) || string.IsNullOrWhiteSpace(sleepAverageDuration.Text) || string.IsNullOrWhiteSpace(sleepTypicalTime.Text))
                throw new InvalidOperationException("All three sleep overview metrics must remain available.");

            sleepRhythm.SelectInterval(smokeSleepId); await ShowSleepDetail(); await Task.Delay(80); UpdateLayout();
            if (sleepSelectedId != smokeSleepId || sleepDetailChart.From != record.Interval.From || sleepDetailChart.To != record.Interval.To ||
                sleepDetailChart.Points.Count == 0 || !sleepDetailChart.Average.HasValue || Math.Abs(sleepDetailChart.Average.Value - 70) > 0.001)
                throw new InvalidOperationException("Selecting the upper sleep interval must show its exact heart-rate range and valid-sample mean below.");
            if (sleepDetailChart.Points.Any(point => point.UnixMs < record.Interval.From || point.UnixMs >= record.Interval.To || point.Bpm <= 0 || point.Bpm >= 200))
                throw new InvalidOperationException("The selected sleep graph must omit out-of-range and invalid measurements.");
            VerifySleepPageFits();

            sleepRhythm.Focus();
            var previousSleepKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sleepRhythm), Environment.TickCount, Key.Left) { RoutedEvent = Keyboard.KeyDownEvent };
            sleepRhythm.RaiseEvent(previousSleepKey); await ShowSleepDetail(); await Task.Delay(40); UpdateLayout();
            if (sleepSelectedId != smokeSleepEmptyId || sleepDetailChart.Points.Count != 0 || sleepDetailChart.Average.HasValue ||
                sleepDetailChart.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(sleepDetail.Text) || !previousSleepKey.Handled)
                throw new InvalidOperationException("Keyboard selection of sleep without heart-rate data must clear the previous graph and keep a visible empty state.");

            var nextSleepKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sleepRhythm), Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent };
            sleepRhythm.RaiseEvent(nextSleepKey); await ShowSleepDetail();
            if (sleepSelectedId != smokeSleepId || !nextSleepKey.Handled || sleepDetailChart.Points.Count == 0)
                throw new InvalidOperationException("Keyboard navigation must return to the next sleep interval and restore its heart-rate graph.");
            int rangeChoice = sleepRangeChoice.SelectedIndex;
            string fromDate = sleepFromDate.Text, toDate = sleepToDate.Text;
            tabs.SelectedIndex = 1; await Task.Delay(40); tabs.SelectedIndex = 2;
            await RefreshSleepManagement(); await ShowSleepDetail(); await Task.Delay(40); UpdateLayout();
            if (sleepRangeChoice.SelectedIndex != rangeChoice || sleepFromDate.Text != fromDate || sleepToDate.Text != toDate || sleepSelectedId != smokeSleepId)
                throw new InvalidOperationException("Returning to Sleep must preserve the period and chart selection.");

            sleepRangeChoice.SelectedIndex = 1; await RefreshSleepManagement(); await Task.Delay(40); UpdateLayout();
            if ((sleepRhythm.LastDay.Date - sleepRhythm.FirstDay.Date).Days > 6)
                throw new InvalidOperationException("Long sleep summary periods must page the timeline instead of overflowing the screen.");
            VerifySleepPageFits();
            await ChangeSleepRhythmPage(-1); await Task.Delay(40); UpdateLayout();
            if ((sleepRhythm.LastDay.Date - sleepRhythm.FirstDay.Date).Days > 6)
                throw new InvalidOperationException("Previous sleep timeline pages must retain their one-screen date span.");
            VerifySleepPageFits(); await ChangeSleepRhythmPage(1);
            sleepRangeChoice.SelectedIndex = 2;
            sleepFromDate.Text = DateTime.Today.AddDays(-29).ToString("yyyy/MM/dd");
            sleepToDate.Text = DateTime.Today.ToString("yyyy/MM/dd");
            await ApplySleepRange(); await Task.Delay(40); UpdateLayout();
            VerifySleepPageFits();
            var previousSleepStatus = sleepMonitor.Status;
            var publishSleepStatus = typeof(OyasumiSleepMonitor).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic);
            try {
                publishSleepStatus.Invoke(sleepMonitor, new object[] { "Unavailable", "isolated smoke" }); UpdateSleepStatus();
                await Task.Delay(40); UpdateLayout();
                if (sleepManagementConnection.Visibility != Visibility.Visible || sleepStatus.Visibility != Visibility.Collapsed ||
                    sleepManagementConnection.ToolTip?.ToString().Contains("isolated smoke") != true)
                    throw new InvalidOperationException("Sleep integration failures must remain visible with diagnostic details on the compact page.");
                Point warningCorner = sleepManagementConnection.TranslatePoint(new Point(), sleepPage);
                if (warningCorner.X < -1 || warningCorner.Y < -1 || warningCorner.X + sleepManagementConnection.ActualWidth > sleepPage.ActualWidth + 1 ||
                    warningCorner.Y + sleepManagementConnection.ActualHeight > sleepPage.ActualHeight + 1)
                    throw new InvalidOperationException("The sleep integration warning must fit within its single-screen footer.");
                VerifySleepPageFits();
            } finally {
                publishSleepStatus.Invoke(sleepMonitor, new object[] { previousSleepStatus.State, previousSleepStatus.Detail }); UpdateSleepStatus();
            }
            sleepRangeChoice.SelectedIndex = rangeChoice;
            sleepFromDate.Text = fromDate; sleepToDate.Text = toDate;
            await RefreshSleepManagement(); sleepRhythm.SelectInterval(smokeSleepId); await ShowSleepDetail();

            foreach (string field in new[] { "sleepRecords", "sleepDeleteButton", "sleepExcludeButton", "sleepHistoryButton" })
                if (GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic) != null)
                    throw new InvalidOperationException("Sleep must not retain the removed record list or management actions: " + field);
            if (GetType().GetMethod("DeleteSelectedSleep", BindingFlags.Instance | BindingFlags.NonPublic) != null)
                throw new InvalidOperationException("Sleep record deletion must not remain callable from the window.");
            if (sleepAnalysis.Records.Count != recordCount)
                throw new InvalidOperationException("Viewing and selecting sleep intervals must preserve every stored sleep record.");
            using (var store = new Store(database))
                if (store.Summary(record.Interval.From, record.Interval.To).Count != 2)
                    throw new InvalidOperationException("Sleep chart navigation must preserve original heart-rate readings.");
            await Task.Delay(80); UpdateLayout(); VerifySleepPageFits();
        }

        private void VerifySleepPageFits()
        {
            if (sleepPage == null || sleepPage.ActualHeight < 1 || sleepPage.DesiredSize.Width > sleepPage.ActualWidth + 1 ||
                sleepPage.DesiredSize.Height > sleepPage.ActualHeight + 1)
                throw new InvalidOperationException("The sleep page must fit its viewport without page scrolling.");
            foreach (FrameworkElement chart in new FrameworkElement[] { sleepRhythm, sleepDetailChart }) {
                Point corner = chart.TranslatePoint(new Point(), sleepPage);
                if (chart.ActualWidth < 250 || chart.ActualHeight < 100 || corner.X < -1 || corner.Y < -1 ||
                    corner.X + chart.ActualWidth > sleepPage.ActualWidth + 1 || corner.Y + chart.ActualHeight > sleepPage.ActualHeight + 1)
                    throw new InvalidOperationException("Both sleep charts must remain visible within one screen.");
            }
        }
    }
}
