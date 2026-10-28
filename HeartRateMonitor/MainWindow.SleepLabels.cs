using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private bool sleepLabelListMode, sleepLabelSelectionUpdating, sleepLabelDeleting;
        private long? sleepLabelDeleteId;
        private Task<bool> sleepLabelWrite = Task.FromResult(false);
        private ListBox sleepLabelList;
        private Grid sleepLabelContent, sleepLabelListView;
        private StackPanel sleepRhythmPaging;
        private Border sleepLabelConfirmation;
        private TextBlock sleepLabelCount, sleepLabelEmpty, sleepLabelDeleteTarget, sleepLabelActionStatus;
        private Button sleepLabelDelete, sleepLabelCancel, sleepLabelConfirm;
        private RadioButton sleepRhythmOption, sleepLabelsOption;

        public sealed class SleepLabelRow : INotifyPropertyChanged
        {
            private string range, duration, state;
            public long Id { get; set; }
            public string Range { get => range; set { if (range != value) { range = value; Changed(nameof(Range)); } } }
            public string Duration { get => duration; set { if (duration != value) { duration = value; Changed(nameof(Duration)); } } }
            public string State { get => state; set { if (state != value) { state = value; Changed(nameof(State)); } } }
            public string AccessibleText => Range + " · " + Duration + (string.IsNullOrEmpty(State) ? "" : " · " + State);
            public event PropertyChangedEventHandler PropertyChanged;
            private void Changed(string property) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property)); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleText))); }
        }

        private UIElement SleepLabelViewSwitch()
        {
            sleepLabelDeleteId = null;
            sleepRhythmOption = new RadioButton { Content = T("リズム", "Rhythm"), GroupName = "SleepView", IsChecked = !sleepLabelListMode, Height = 30, Style = (Style)FindResource("TimeRangeOption") };
            sleepLabelsOption = new RadioButton { Content = T("記録一覧", "Records"), GroupName = "SleepView", IsChecked = sleepLabelListMode, Height = 30, Style = (Style)FindResource("TimeRangeOption") };
            AutomationProperties.SetAutomationId(sleepRhythmOption, "SleepRhythmView"); AutomationProperties.SetAutomationId(sleepLabelsOption, "SleepLabelsView");
            sleepRhythmOption.Checked += async (s, e) => await ChangeSleepLabelView(false);
            sleepLabelsOption.Checked += async (s, e) => await ChangeSleepLabelView(true);
            return Horizontal(sleepRhythmOption, sleepLabelsOption);
        }

        private UIElement SleepLabelContent()
        {
            sleepLabelList = new ListBox { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Single,
                ItemContainerStyle = (Style)FindResource("SleepLabelItem"), ItemTemplate = (DataTemplate)FindResource("SleepLabelTemplate") };
            sleepLabelList.SetResourceReference(Control.ForegroundProperty, "Text");
            ScrollViewer.SetHorizontalScrollBarVisibility(sleepLabelList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(sleepLabelList, ScrollBarVisibility.Auto);
            VirtualizingPanel.SetIsVirtualizing(sleepLabelList, true);
            VirtualizingPanel.SetVirtualizationMode(sleepLabelList, VirtualizationMode.Recycling);
            sleepLabelList.ItemsSource = new ObservableCollection<SleepLabelRow>();
            AutomationProperties.SetAutomationId(sleepLabelList, "SleepLabelList");
            AutomationProperties.SetName(sleepLabelList, T("選択期間の睡眠ラベル、新しい順", "Sleep labels in the selected period, newest first"));
            sleepLabelList.SelectionChanged += async (s, e) => {
                if (sleepLabelSelectionUpdating) return;
                sleepSelectedId = (sleepLabelList.SelectedItem as SleepLabelRow)?.Id;
                CancelSleepLabelDeletion(false); sleepLabelActionStatus.Text = "";
                await ShowSleepDetail();
            };
            sleepLabelCount = Label("", 11); sleepLabelCount.Margin = new Thickness(0, 0, 0, 4);
            AutomationProperties.SetAutomationId(sleepLabelCount, "SleepLabelCount");
            sleepLabelEmpty = Label(T("この期間に睡眠ラベルはありません。期間を変更すると過去の記録を確認できます。", "No sleep labels in this period. Change the period to find older records."), 13);
            sleepLabelEmpty.HorizontalAlignment = HorizontalAlignment.Center; sleepLabelEmpty.Margin = new Thickness(16);
            AutomationProperties.SetAutomationId(sleepLabelEmpty, "SleepLabelEmpty");
            var listGrid = new Grid(); sleepLabelListView = listGrid;
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); listGrid.RowDefinitions.Add(new RowDefinition());
            listGrid.Children.Add(sleepLabelCount); Grid.SetRow(sleepLabelList, 1); listGrid.Children.Add(sleepLabelList);
            Grid.SetRow(sleepLabelEmpty, 1); listGrid.Children.Add(sleepLabelEmpty);
            sleepLabelContent = new Grid(); sleepLabelContent.Children.Add(sleepRhythm); sleepLabelContent.Children.Add(listGrid);
            AutomationProperties.SetAutomationId(listGrid, "SleepLabelListView");

            sleepLabelDeleteTarget = Label("", 13, "Text"); sleepLabelDeleteTarget.FontWeight = FontWeights.SemiBold;
            sleepLabelDeleteTarget.Margin = new Thickness(0, 8, 0, 8);
            AutomationProperties.SetAutomationId(sleepLabelDeleteTarget, "SleepLabelDeleteTarget");
            sleepLabelCancel = SleepLabelButton(T("キャンセル", "Cancel"), (s, e) => CancelSleepLabelDeletion(true), "SleepLabelCancel", 110);
            sleepLabelConfirm = SleepLabelButton(T("ラベルを削除", "Delete label"), async (s, e) => await ConfirmSleepLabelDeletion(), "SleepLabelConfirm", 120);
            sleepLabelConfirm.SetResourceReference(Control.ForegroundProperty, "Danger"); sleepLabelConfirm.SetResourceReference(Control.BorderBrushProperty, "Danger");
            sleepLabelConfirm.SetResourceReference(Control.BackgroundProperty, "DangerSoft");
            var actions = Horizontal(sleepLabelCancel, sleepLabelConfirm); actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new Thickness(0, 12, 0, 0);
            var prompt = Stack(Label(T("この睡眠ラベルを削除しますか？", "Delete this sleep label?"), 15, "Text"), sleepLabelDeleteTarget,
                Label(T("心拍記録は残ります。ラベルの削除は元に戻せません。", "Heart-rate readings will remain. Deleting the label cannot be undone."), 12), actions);
            prompt.VerticalAlignment = VerticalAlignment.Center;
            sleepLabelConfirmation = new Border { Child = prompt, Padding = new Thickness(14, 6, 14, 6), Visibility = Visibility.Collapsed };
            sleepLabelConfirmation.SetResourceReference(Border.BackgroundProperty, "Surface");
            AutomationProperties.SetAutomationId(sleepLabelConfirmation, "SleepLabelConfirmation");
            KeyboardNavigation.SetTabNavigation(sleepLabelConfirmation, KeyboardNavigationMode.Cycle);
            sleepLabelConfirmation.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && !sleepLabelDeleting) { CancelSleepLabelDeletion(true); e.Handled = true; } };
            sleepLabelContent.Children.Add(sleepLabelConfirmation);
            return sleepLabelContent;
        }

        private UIElement SleepLabelDetailHeader()
        {
            sleepLabelDelete = SleepLabelButton(T("ラベルを削除…", "Delete label…"), (s, e) => RequestSleepLabelDeletion(), "SleepLabelDelete", 126);
            sleepLabelDelete.Margin = new Thickness(12, 0, 0, 0);
            ToolTipService.SetShowOnDisabled(sleepLabelDelete, true);
            var heading = new DockPanel(); DockPanel.SetDock(sleepLabelDelete, Dock.Right); heading.Children.Add(sleepLabelDelete); heading.Children.Add(sleepDetail);
            sleepLabelActionStatus = Label("", 11); sleepLabelActionStatus.Margin = new Thickness(0, 2, 0, 0);
            AutomationProperties.SetAutomationId(sleepLabelActionStatus, "SleepLabelActionStatus");
            AutomationProperties.SetLiveSetting(sleepLabelActionStatus, AutomationLiveSetting.Polite);
            return Stack(heading, sleepLabelActionStatus);
        }

        private Button SleepLabelButton(string text, RoutedEventHandler handler, string id, double width)
        {
            var button = Button(text, handler, id: id, width: width);
            button.Height = 30; button.Margin = new Thickness(6, 0, 0, 0); button.Style = (Style)FindResource("ConnectionAction");
            return button;
        }

        private async Task ChangeSleepLabelView(bool list)
        {
            if (sleepLabelDeleting || sleepLabelList == null) return;
            CancelSleepLabelDeletion(false); sleepLabelListMode = list; UpdateSleepLabelView();
            if (sleepAnalysis == null) return;
            // When returning to the rhythm, show the page containing the selected label.
            var record = sleepAnalysis.Records.FirstOrDefault(r => r.Interval.Id == sleepSelectedId);
            if (!list && record != null) {
                var day = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(record.Interval.From), TimeZoneInfo.Local).DateTime.AddHours(-settings.SleepDayBoundaryHour).Date;
                day = day < sleepRangeFirstDay ? sleepRangeFirstDay : day > sleepRangeLastDay ? sleepRangeLastDay : day;
                sleepRhythmPageOffset = (sleepRangeLastDay - day).Days / 7 * 7;
            }
            await ShowSleepRhythmPage();
            if (list && sleepLabelList.SelectedItem != null) sleepLabelList.ScrollIntoView(sleepLabelList.SelectedItem);
        }

        private void UpdateSleepLabelView()
        {
            sleepRhythm.Visibility = sleepLabelListMode ? Visibility.Collapsed : Visibility.Visible;
            sleepLabelListView.Visibility = sleepLabelListMode ? Visibility.Visible : Visibility.Collapsed;
            sleepRhythmPaging.Visibility = sleepLabelListMode ? Visibility.Collapsed : Visibility.Visible;
            sleepLabelDelete.Visibility = sleepLabelListMode ? Visibility.Visible : Visibility.Collapsed;
            sleepRhythmOption.IsEnabled = sleepLabelsOption.IsEnabled = !sleepLabelDeleting && !sleepLabelDeleteId.HasValue;
        }

        private void UpdateSleepLabelRows()
        {
            var rows = sleepAnalysis.Records.OrderByDescending(r => r.Interval.From).ThenByDescending(r => r.Interval.Id).Select(r => new SleepLabelRow {
                Id = r.Interval.Id, Range = SleepLabelRange(r.Interval), Duration = SleepDuration(r.DurationMilliseconds),
                State = r.Interval.IsOngoing ? T("記録中", "Recording") : r.Interval.Excluded ? T("集計対象外", "Excluded from averages") : T("記録完了", "Completed")
            }).ToList();
            // Update rows in place so recording ticks preserve keyboard focus and scroll.
            var existing = (ObservableCollection<SleepLabelRow>)sleepLabelList.ItemsSource;
            sleepLabelSelectionUpdating = true;
            try {
                for (int i = existing.Count - 1; i >= 0; i--) if (!rows.Any(row => row.Id == existing[i].Id)) existing.RemoveAt(i);
                for (int i = 0; i < rows.Count; i++) {
                    var row = existing.FirstOrDefault(item => item.Id == rows[i].Id);
                    if (row == null) existing.Insert(i, rows[i]);
                    else {
                        int previous = existing.IndexOf(row); if (previous != i) existing.Move(previous, i);
                        row.Range = rows[i].Range; row.Duration = rows[i].Duration; row.State = rows[i].State;
                    }
                }
            } finally { sleepLabelSelectionUpdating = false; }
            sleepLabelCount.Text = T($"{rows.Count:N0}件 · 新しい順 · 選択すると下に心拍グラフを表示", $"{rows.Count:N0} labels · Newest first · Select to view heart rate below");
            sleepLabelEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string SleepLabelRange(SleepInterval interval) =>
            DateTimeOffset.FromUnixTimeMilliseconds(interval.From).LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss") + " → " +
            DateTimeOffset.FromUnixTimeMilliseconds(interval.To).LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss");

        private void SynchronizeSleepLabelSelection(SleepRecord record)
        {
            var row = sleepLabelList.Items.Cast<SleepLabelRow>().FirstOrDefault(r => r.Id == record?.Interval.Id);
            bool changed = !ReferenceEquals(sleepLabelList.SelectedItem, row);
            sleepLabelSelectionUpdating = true;
            try { sleepLabelList.SelectedItem = row; } finally { sleepLabelSelectionUpdating = false; }
            if (changed && row != null && sleepLabelListMode) sleepLabelList.ScrollIntoView(row);
            if (sleepLabelDeleteId.HasValue && (record == null || record.Interval.Id != sleepLabelDeleteId || record.Interval.IsOngoing)) CancelSleepLabelDeletion(false);
            sleepLabelDelete.IsEnabled = record != null && !record.Interval.IsOngoing && !sleepLabelDeleting && !sleepManagementLoading && !sleepLabelDeleteId.HasValue;
            sleepLabelDelete.ToolTip = record?.Interval.IsOngoing == true ? T("記録中のラベルは削除できません。睡眠モード終了後に操作してください。", "A recording label cannot be deleted. Wait until sleep mode ends.") : T("選択したラベルだけを削除します。心拍記録は残ります。", "Delete only the selected label. Heart-rate readings will remain.");
        }

        private void RequestSleepLabelDeletion()
        {
            var record = sleepAnalysis?.Records.FirstOrDefault(r => r.Interval.Id == sleepSelectedId);
            if (record == null || record.Interval.IsOngoing || sleepLabelDeleting || sleepManagementLoading) return;
            sleepLabelDeleteId = record.Interval.Id; sleepLabelDeleteTarget.Text = SleepLabelRange(record.Interval);
            sleepLabelActionStatus.Text = ""; sleepLabelConfirmation.Visibility = Visibility.Visible;
            sleepLabelList.IsEnabled = false; sleepLabelDelete.IsEnabled = false; UpdateSleepLabelView();
            sleepLabelCancel.Focus();
        }

        private void CancelSleepLabelDeletion(bool focus)
        {
            if (sleepLabelDeleting || sleepLabelConfirmation == null) return;
            sleepLabelDeleteId = null; sleepLabelConfirmation.Visibility = Visibility.Collapsed; sleepLabelList.IsEnabled = true;
            UpdateSleepLabelView();
            var record = sleepAnalysis?.Records.FirstOrDefault(r => r.Interval.Id == sleepSelectedId);
            sleepLabelDelete.IsEnabled = record != null && !record.Interval.IsOngoing && !sleepManagementLoading;
            if (focus) sleepLabelDelete.Focus();
        }

        private async Task ConfirmSleepLabelDeletion()
        {
            if (!sleepLabelDeleteId.HasValue || sleepLabelDeleting || closing) return;
            long id = sleepLabelDeleteId.Value;
            var ordered = sleepAnalysis.Records.OrderByDescending(r => r.Interval.From).ThenByDescending(r => r.Interval.Id).ToList();
            int index = ordered.FindIndex(r => r.Interval.Id == id);
            long? adjacent = index >= 0 && ordered.Count > 1 ? (long?)ordered[index + 1 < ordered.Count ? index + 1 : index - 1].Interval.Id : null;
            sleepLabelDeleting = true; sleepPage.IsEnabled = false;
            bool deleted = false; string message;
            try {
                sleepLabelWrite = Task.Run(() => { using (var store = new Store(database)) return store.DeleteSleepInterval(id); });
                deleted = await sleepLabelWrite;
                message = deleted ? T("ラベルを削除しました。心拍記録は残っています。", "Label deleted. Heart-rate readings were kept.") :
                    T("削除できませんでした。記録中、または既に削除されたラベルです。一覧を更新しました。", "Could not delete: the label is recording or already removed. The list has been refreshed.");
            } catch (Exception ex) {
                connectionLog.Add("Sleep label deletion: " + ex);
                message = T("ラベルを削除できません: ", "Could not delete label: ") + ex.Message;
            } finally { sleepLabelDeleting = false; if (!closing) sleepPage.IsEnabled = true; }
            if (closing) return;
            CancelSleepLabelDeletion(false);
            if (deleted) { sleepSelectedId = adjacent; sleepHistoryDirty = true; }
            await RefreshSleepManagement();
            if (deleted) await RefreshSleepHistory();
            sleepLabelActionStatus.Text = message;
            sleepLabelList.Focus();
        }
    }
}
