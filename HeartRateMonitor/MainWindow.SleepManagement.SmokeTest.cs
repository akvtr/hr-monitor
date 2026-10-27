using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private long smokeSleepId;

        private async Task VerifySleepManagement()
        {
            if (!smokeTest) throw new InvalidOperationException("Sleep-management fixtures require isolated smoke mode.");
            if (smokeSleepId == 0) {
                // Explicit --smoke-test only: these are labeled test readings, never normal data.
                long start = new DateTimeOffset(DateTime.Today.AddDays(-2).AddHours(1)).ToUnixTimeMilliseconds();
                using (var store = new Store(database)) {
                    var sleep = new SleepInterval { From = start, To = start + 6 * 3600000L };
                    store.SaveSleepInterval(sleep); smokeSleepId = sleep.Id;
                    store.SaveSleepInterval(new SleepInterval { From = start - 86400000L, To = start - 86400000L + 5 * 3600000L });
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
            var record = sleepRecords.Items.Cast<SleepRecord>().Single(item => item.Interval.Id == smokeSleepId);
            sleepRecords.SelectedItem = record;
            await Task.Delay(100); UpdateLayout();
            if (record.Summary.Count != 2 || Math.Abs(record.Summary.Average - 70) > 0.001)
                throw new InvalidOperationException("Sleep details must average only valid measured heart rates.");
            if (string.IsNullOrWhiteSpace(sleepAverageHeart.Text) || string.IsNullOrWhiteSpace(sleepAverageDuration.Text) ||
                string.IsNullOrWhiteSpace(sleepTypicalTime.Text) || string.IsNullOrWhiteSpace(sleepDetail.Text) ||
                !sleepHistoryButton.IsEnabled || !sleepExcludeButton.IsEnabled || !sleepDeleteButton.IsEnabled)
                throw new InvalidOperationException("Sleep overview, details and finished-record actions must be available.");
            if (sleepRhythm.ActualWidth < 250 || sleepRecords.ActualWidth < 250 ||
                sleepRecords.DesiredSize.Width > sleepRecords.ActualWidth + 1)
                throw new InvalidOperationException("Sleep rhythm and records must fit the compact page width.");

            string includedHeart = sleepAverageHeart.Text, includedDuration = sleepAverageDuration.Text;
            await ChangeSleepExclusion();
            using (var store = new Store(database)) {
                if (!store.SleepIntervals(record.Interval.From, record.Interval.To).Single(item => item.Id == smokeSleepId).Excluded)
                    throw new InvalidOperationException("Sleep exclusion must persist when the database is reopened.");
            }
            if (!((SleepRecord)sleepRecords.SelectedItem).Interval.Excluded)
                throw new InvalidOperationException("Excluded sleep records must remain selected and visible for management.");
            if (sleepAverageHeart.Text == includedHeart || sleepAverageDuration.Text == includedDuration)
                throw new InvalidOperationException("Excluding a sleep record must update overview heart rate and duration.");
            await ChangeSleepExclusion();
            using (var store = new Store(database)) {
                if (store.SleepIntervals(record.Interval.From, record.Interval.To).Single(item => item.Id == smokeSleepId).Excluded)
                    throw new InvalidOperationException("Reincluding sleep must persist without deleting the label.");
            }
            if (sleepAverageHeart.Text != includedHeart || sleepAverageDuration.Text != includedDuration)
                throw new InvalidOperationException("Reincluding sleep must restore the overview aggregates.");

            int rangeChoice = sleepRangeChoice.SelectedIndex;
            string fromDate = sleepFromDate.Text, toDate = sleepToDate.Text;
            await OpenSelectedSleepHistory();
            if (tabs.SelectedIndex != 1 || historyWindow.From > record.Interval.From || historyWindow.To < record.Interval.To)
                throw new InvalidOperationException("Opening sleep in History must display the selected interval.");
            tabs.SelectedIndex = 2; await RefreshSleepManagement(); await Task.Delay(80); UpdateLayout();
            if (sleepRangeChoice.SelectedIndex != rangeChoice || sleepFromDate.Text != fromDate || sleepToDate.Text != toDate ||
                (sleepRecords.SelectedItem as SleepRecord)?.Interval.Id != smokeSleepId)
                throw new InvalidOperationException("Returning from History must preserve sleep period and selection.");

            using (var store = new Store(database)) {
                var remove = new SleepInterval { From = record.Interval.From + 7 * 3600000L, To = record.Interval.From + 8 * 3600000L };
                store.SaveSleepInterval(remove);
                await RefreshSleepManagement();
                sleepRecords.SelectedItem = sleepRecords.Items.Cast<SleepRecord>().Single(item => item.Interval.Id == remove.Id);
                await DeleteSelectedSleep(false);
                if (store.SleepIntervals(remove.From, remove.To).Any(item => item.Id == remove.Id))
                    throw new InvalidOperationException("Deleting a sleep label must remove the selected interval.");
                if (store.Summary(record.Interval.From, record.Interval.To).Count != 2)
                    throw new InvalidOperationException("Deleting sleep labels must preserve original heart-rate readings.");
                var ongoing = new SleepInterval { From = record.Interval.From + 9 * 3600000L, To = record.Interval.From + 10 * 3600000L, IsOngoing = true };
                store.SaveSleepInterval(ongoing);
                await RefreshSleepManagement();
                sleepRecords.SelectedItem = sleepRecords.Items.Cast<SleepRecord>().Single(item => item.Interval.Id == ongoing.Id);
                if (sleepExcludeButton.IsEnabled || sleepDeleteButton.IsEnabled || !sleepHistoryButton.IsEnabled)
                    throw new InvalidOperationException("An ongoing sleep interval must remain viewable while destructive actions are disabled.");
                ongoing.IsOngoing = false; store.SaveSleepInterval(ongoing); store.DeleteSleepInterval(ongoing.Id);
            }
            await RefreshSleepManagement();
            sleepRecords.SelectedItem = sleepRecords.Items.Cast<SleepRecord>().Single(item => item.Interval.Id == smokeSleepId);
            await Task.Delay(80); UpdateLayout();
        }
    }
}
