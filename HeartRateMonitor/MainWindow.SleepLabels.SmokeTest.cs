using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private async Task VerifySleepLabelManagement()
        {
            if (!smokeTest) throw new InvalidOperationException("Sleep-label fixtures require isolated smoke mode.");
            int originalRange = sleepRangeIndex, originalOffset = sleepRhythmPageOffset;
            DateTime? originalFrom = sleepCustomFrom, originalTo = sleepCustomTo;
            string originalFromText = sleepFromDate.Text, originalToText = sleepToDate.Text;
            long? originalSelected = sleepSelectedId;
            var originalHistory = historyWindow;
            bool originalListMode = sleepLabelListMode;
            var day = DateTime.Today.AddDays(-40);
            long start = Store.SleepDayBoundary(day, TimeZoneInfo.Local, settings.SleepDayBoundaryHour);
            var first = new SleepInterval { From = start + 3600000, To = start + 7200000 };
            var second = new SleepInterval { From = start + 3 * 3600000, To = start + 5 * 3600000 };
            var ongoing = new SleepInterval { From = start + 6 * 3600000, To = start + 7 * 3600000, IsOngoing = true };
            long originalReadingCount;
            using (var store = new Store(database)) {
                originalReadingCount = store.Summary(start, start + 86400000).Count;
                store.SaveSleepInterval(first); store.SaveSleepInterval(second); store.SaveSleepInterval(ongoing);
                foreach (var reading in new[] {
                    new Reading { UnixMs = first.From + 60000, Bpm = 60 },
                    new Reading { UnixMs = second.From + 60000, Bpm = 90 },
                    new Reading { UnixMs = ongoing.From + 60000, Bpm = 100 }
                }) {
                    reading.Device = "ISOLATED SLEEP LABEL UI TEST"; reading.Session = "smoke-sleep-labels"; store.Add(reading);
                }
            }
            try {
                sleepRangeChoice.SelectedIndex = 2;
                sleepFromDate.Text = sleepToDate.Text = day.ToString("yyyy/MM/dd");
                await ApplySleepRange();
                sleepLabelsOption.IsChecked = true; await ChangeSleepLabelView(true);
                await Task.Delay(40); UpdateLayout();
                var rows = sleepLabelList.Items.Cast<SleepLabelRow>().ToList();
                if (rows.Count != 3 || rows[0].Id != ongoing.Id || rows[1].Id != second.Id || rows[2].Id != first.Id)
                    throw new InvalidOperationException("The sleep-label list must show each real interval once, newest first.");
                sleepLabelList.SelectedItem = rows[1]; await ShowSleepDetail(); await Task.Delay(40); UpdateLayout();
                if (sleepSelectedId != second.Id || sleepDetailChart.From != second.From || sleepDetailChart.To != second.To ||
                    sleepDetailChart.Points.Count == 0 || sleepDetailChart.Average != 90 || !sleepLabelDelete.IsEnabled)
                    throw new InvalidOperationException("Selecting a sleep-label row must display its exact measured heart-rate interval and enable its deletion action.");
                VerifySleepPageFits();
                sleepSmokeCapture?.Invoke($"sleep-records-{settings.Language}-{settings.Theme.ToLowerInvariant()}");
                var selectedContainer = (ListBoxItem)sleepLabelList.ItemContainerGenerator.ContainerFromItem(rows[1]);
                selectedContainer.Focus();
                using (var store = new Store(database)) { ongoing.To += 1000; store.SaveSleepInterval(ongoing); }
                await RefreshSleepManagement(); await Task.Delay(40); UpdateLayout();
                if (!ReferenceEquals(sleepLabelList.SelectedItem, rows[1]) ||
                    !ReferenceEquals(sleepLabelList.ItemContainerGenerator.ContainerFromItem(rows[1]), selectedContainer) || !selectedContainer.IsKeyboardFocusWithin)
                    throw new InvalidOperationException("Refreshing a recording interval must preserve the selected row object, container and keyboard focus.");
                int selectedOffset = sleepRhythmPageOffset;
                sleepRhythmOption.IsChecked = true; await ChangeSleepLabelView(false);
                sleepLabelsOption.IsChecked = true; await ChangeSleepLabelView(true);
                if (sleepSelectedId != second.Id || sleepRhythmPageOffset != selectedOffset || ((SleepLabelRow)sleepLabelList.SelectedItem).Id != second.Id)
                    throw new InvalidOperationException("Switching sleep views must retain the selected interval and its rhythm page.");

                RequestSleepLabelDeletion(); await Task.Delay(40); UpdateLayout();
                if (sleepLabelConfirmation.Visibility != Visibility.Visible || sleepLabelDeleteId != second.Id ||
                    sleepLabelDeleteTarget.Text != SleepLabelRange(second) ||
                    !ReferenceEquals(FocusManager.GetFocusedElement(this), sleepLabelCancel) || sleepLabelList.IsEnabled)
                    throw new InvalidOperationException("Deletion confirmation must identify the complete interval, focus Cancel and suspend list selection.");
                VerifySleepPageFits();
                sleepSmokeCapture?.Invoke($"sleep-record-confirmation-{settings.Language}-{settings.Theme.ToLowerInvariant()}");
                var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sleepLabelCancel), Environment.TickCount, Key.Escape) {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                sleepLabelConfirmation.RaiseEvent(escape);
                if (!escape.Handled || sleepLabelConfirmation.Visibility != Visibility.Collapsed || sleepLabelDeleteId.HasValue || !sleepLabelList.IsEnabled)
                    throw new InvalidOperationException("Escape must cancel label deletion and restore the list without deleting data.");
                using (var store = new Store(database))
                    if (store.SleepIntervals(start, start + 86400000).Count != 3)
                        throw new InvalidOperationException("Cancelling a sleep-label deletion must leave all stored intervals intact.");

                sleepLabelList.SelectedItem = rows[0]; await ShowSleepDetail();
                RequestSleepLabelDeletion();
                if (sleepLabelDelete.IsEnabled || sleepLabelDeleteId.HasValue || sleepLabelConfirmation.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("An ongoing label must have a disabled deletion action and cannot open confirmation.");
                sleepLabelList.SelectedItem = rows[1]; await ShowSleepDetail();
                historyWindow = new TimeWindow(start, start + 86400000); await RefreshHistory(false);
                if (!historyChart.SleepIntervals.Any(interval => interval.Id == second.Id))
                    throw new InvalidOperationException("The disposable sleep label must be visible in History before testing deletion refresh.");
                RequestSleepLabelDeletion(); await ConfirmSleepLabelDeletion();
                if (sleepLabelList.Items.Count != 2 || sleepSelectedId != first.Id || sleepAnalysis.AverageHeartRate != 60 ||
                    sleepAnalysis.AverageDurationMilliseconds != 3600000 || historyChart.SleepIntervals.Any(interval => interval.Id == second.Id) ||
                    sleepLabelConfirmation.Visibility != Visibility.Collapsed || !sleepLabelList.IsEnabled || string.IsNullOrEmpty(sleepLabelActionStatus.Text))
                    throw new InvalidOperationException("Confirmed deletion must select the adjacent row and refresh the list, averages, History and completion feedback.");
                using (var store = new Store(database))
                    if (store.Summary(start, start + 86400000).Count != originalReadingCount + 3)
                        throw new InvalidOperationException("Deleting sleep labels must preserve all original heart-rate samples.");
                RequestSleepLabelDeletion(); await ConfirmSleepLabelDeletion();
                if (sleepSelectedId != ongoing.Id || sleepLabelDelete.IsEnabled)
                    throw new InvalidOperationException("Deleting the oldest label must select its adjacent remaining ongoing label safely.");
                using (var store = new Store(database)) { ongoing.IsOngoing = false; store.SaveSleepInterval(ongoing); }
                await RefreshSleepManagement(); RequestSleepLabelDeletion(); await ConfirmSleepLabelDeletion();
                await Task.Delay(40); UpdateLayout();
                if (sleepLabelList.Items.Count != 0 || sleepSelectedId.HasValue || sleepLabelEmpty.Visibility != Visibility.Visible ||
                    sleepLabelDelete.IsEnabled || sleepDetailChart.Points.Count != 0 || sleepDetailChart.Average.HasValue ||
                    sleepAnalysis.AverageHeartRate.HasValue || sleepAnalysis.AverageDurationMilliseconds.HasValue)
                    throw new InvalidOperationException("Deleting the last label must show an empty list and clear selection, graph and summary values.");
                VerifySleepPageFits();
                sleepSmokeCapture?.Invoke($"sleep-records-empty-{settings.Language}-{settings.Theme.ToLowerInvariant()}");
            } finally {
                CancelSleepLabelDeletion(false);
                using (var store = new Store(database)) {
                    ongoing.IsOngoing = false; store.SaveSleepInterval(ongoing);
                    foreach (var interval in new[] { first, second, ongoing }) store.DeleteSleepInterval(interval.Id);
                }
                sleepCustomFrom = originalFrom; sleepCustomTo = originalTo;
                sleepFromDate.Text = originalFromText; sleepToDate.Text = originalToText;
                sleepRangeChoice.SelectedIndex = originalRange;
                sleepSelectedId = originalSelected;
                await RefreshSleepManagement(); sleepRhythmPageOffset = originalOffset;
                sleepRhythmOption.IsChecked = !originalListMode; sleepLabelsOption.IsChecked = originalListMode;
                await ChangeSleepLabelView(originalListMode); await ShowSleepDetail();
                historyWindow = originalHistory; await RefreshHistory(false);
            }
        }
    }
}
