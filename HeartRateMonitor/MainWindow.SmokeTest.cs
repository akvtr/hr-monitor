using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private Action<string> sleepSmokeCapture;
        public void EnableSmokeTest(string path, bool compact = false)
        {
            smokeTest = true;
            Loaded += async (s, e) => {
                if (compact) { Width = MinWidth; Height = MinHeight; }
                Action<string> capture = name => {
                    var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(Path.GetDirectoryName(path), name + ".png"))) encoder.Save(stream);
                };
                sleepSmokeCapture = capture;
                await Task.Delay(300);
                string checksPath = Path.Combine(Path.GetDirectoryName(path), "smoke-checks.txt");
                File.WriteAllText(checksPath, "");
                foreach (var language in new[] { "ja", "en" }) foreach (var mode in new[] { "Dark", "Light", "System" }) {
                    settings.Language = language; settings.Theme = mode; settings.Save(settingsPath);
                    Words.Language = language; BuildUi(); ApplyTheme();
                    for (int i = 0; i < 4; i++) {
                        tabs.SelectedIndex = i;
                        if (i == 1) await RefreshHistory(true);
                        if (i == 2) {
                            await VerifySleepManagement();
                            File.AppendAllText(checksPath, $"PASS {language}/{mode}: sleep metrics, timeline and keyboard selection load the exact heart-rate graph, empty data clears stale points, selection persists, long/custom periods and integration warnings fit one screen, and deletion/list actions are removed.{Environment.NewLine}");
                        }
                        await Task.Delay(150); UpdateLayout();
                        foreach (TabItem item in tabs.Items) {
                            var header = (StackPanel)item.Header;
                            var text = (TextBlock)header.Children[1];
                            if (item.ActualWidth > 100.5 || text.ActualWidth < new FormattedText(text.Text,
                                System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1).Width - 0.5)
                                throw new InvalidOperationException("Compact navigation must fit all translated labels.");
                        }
                        capture($"smoke-{language}-{mode.ToLowerInvariant()}-{i}");
                        double pageWidth = tabs.ActualWidth, pageHeight = tabs.ActualHeight;
                        double windowWidth = ActualWidth, windowHeight = ActualHeight;
                        connectionPopup.IsOpen = true;
                        await Task.Delay(80); UpdateLayout();
                        var popupCard = (FrameworkElement)connectionPopup.Child;
                        if (!connectionPopup.IsOpen || popupCard.ActualWidth < 1 ||
                            Math.Abs(tabs.ActualWidth - pageWidth) > 0.1 || Math.Abs(tabs.ActualHeight - pageHeight) > 0.1 ||
                            Math.Abs(ActualWidth - windowWidth) > 0.1 || Math.Abs(ActualHeight - windowHeight) > 0.1)
                            throw new InvalidOperationException("Connection details must open without resizing the window or page.");
                        if (!ReferenceEquals(((System.Windows.Controls.Border)popupCard).Background, FindResource("Surface")))
                            throw new InvalidOperationException("Connection details must use the active theme.");
                        if (i == 0) {
                            var popupBitmap = new RenderTargetBitmap((int)Math.Ceiling(popupCard.ActualWidth), (int)Math.Ceiling(popupCard.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                            popupBitmap.Render(popupCard);
                            var popupEncoder = new PngBitmapEncoder(); popupEncoder.Frames.Add(BitmapFrame.Create(popupBitmap));
                            using (var stream = File.Create(Path.Combine(Path.GetDirectoryName(path), $"details-{language}-{mode.ToLowerInvariant()}.png"))) popupEncoder.Save(stream);
                        }
                        connectionPopup.IsOpen = false;
                        await Task.Delay(50); UpdateLayout();
                        if (Math.Abs(tabs.ActualWidth - pageWidth) > 0.1 || Math.Abs(tabs.ActualHeight - pageHeight) > 0.1)
                            throw new InvalidOperationException("Closing connection details must preserve page size.");
                        File.AppendAllText(checksPath, $"PASS {language}/{mode}/tab-{i}: details popup preserves window and page dimensions, active theme surface.{Environment.NewLine}");
                        if (i == 3) {
                            if (tabs.Items.Count != 4 || tabs.SelectedIndex != 3)
                                throw new InvalidOperationException("Settings must follow Live, History and Sleep.");
                            if (!closeToTray.IsDescendantOf(settingsSections[0]) || !autoConnect.IsDescendantOf(settingsSections[1]) ||
                                !autoReconnect.IsDescendantOf(settingsSections[1]) || !retentionChoice.IsDescendantOf(settingsSections[2]) ||
                                !oyasumiEnabled.IsDescendantOf(settingsSections[3]) || !oyasumiPort.IsDescendantOf(settingsSections[3]) ||
                                !host.IsDescendantOf(settingsSections[4]) || !percentMin.IsDescendantOf(settingsSections[5]) ||
                                !oscTimeoutMode.IsDescendantOf(settingsSections[5]) || !testButton.IsDescendantOf(settingsSections[6]))
                                throw new InvalidOperationException("Settings must group general behavior, sensors, storage, OyasumiVR and OSC controls by purpose.");
                            string previousHost = host.Text; host.Text = "127.0.0.3";
                            var navigationPosition = settingsLinks[0].TranslatePoint(new Point(), this);
                            for (int category = 0; category < settingsLinks.Length; category++) {
                                settingsLinks[category].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                                await Task.Delay(80); UpdateLayout();
                                var section = settingsSections[category];
                                var position = settingsLinks[0].TranslatePoint(new Point(), this);
                                if (settingsCategory != category || settingsLinks[category].IsChecked != true ||
                                    settingsSections.Count(item => item.IsVisible) != 1 || !section.IsVisible ||
                                    Math.Abs(position.Y - navigationPosition.Y) > 1 || host.Text != "127.0.0.3")
                                    throw new InvalidOperationException("Category selection must replace the form, keep navigation fixed and preserve unsaved edits.");
                                var footer = (FrameworkElement)VisualTreeHelper.GetParent(settingsStatus);
                                double sectionBottom = section.TranslatePoint(new Point(0, section.ActualHeight), tabs).Y;
                                double footerTop = footer.TranslatePoint(new Point(), tabs).Y;
                                if (sectionBottom + 24 > footerTop - 11 || section.DesiredSize.Width > section.ActualWidth + 1)
                                    throw new InvalidOperationException($"Settings category {category} must fit without scrolling: bottom {sectionBottom}, footer {footerTop}, desired width {section.DesiredSize.Width}, actual {section.ActualWidth}.");
                                capture($"smoke-{language}-{mode.ToLowerInvariant()}-settings-category-{category}");
                            }
                            host.Text = previousHost;
                            File.AppendAllText(checksPath, $"PASS {language}/{mode}: all seven settings categories replace content, fit without scrolling, keep navigation fixed and preserve unsaved edits.{Environment.NewLine}");
                            var selected = (TabItem)tabs.Items[3]; selected.ApplyTemplate();
                            var selectedShell = (Border)selected.Template.FindName("Shell", selected);
                            var selectedLine = (Border)selected.Template.FindName("SelectedLine", selected);
                            if (!ReferenceEquals(selectedShell.Background, FindResource("AccentSoft")) || !ReferenceEquals(selected.Foreground, FindResource("Text")) || selectedLine.Visibility != Visibility.Visible)
                                throw new InvalidOperationException("Selected tab must have a soft fill, readable text and a selection line.");
                            NavigateSettings(4);
                            host.Text = "127.0.0.2";
                            tabs.SelectedIndex = 0; await Task.Delay(50); tabs.SelectedIndex = 3; await Task.Delay(80); UpdateLayout();
                            if (host.Text != "127.0.0.2" || settingsCategory != 4 || !settingsSections[4].IsVisible)
                                throw new InvalidOperationException("Switching tabs lost unsaved settings or the selected category.");
                            host.Text = previousHost;
                            NavigateSettings(5);
                            oscTimeoutMode.SelectedIndex = 1;
                            oscTimeoutSeconds.Text = "12";
                            oscTimeoutInt.Text = "42";
                            oscTimeoutFloat.Text = 0.75f.ToString(System.Globalization.CultureInfo.CurrentCulture);
                            if (!oscTimeoutSeconds.IsEnabled || !oscTimeoutInt.IsEnabled || !oscTimeoutFloat.IsEnabled) throw new InvalidOperationException("Fallback mode must enable its interval and value fields.");
                            SaveClicked(null, null); await Task.Delay(200); UpdateLayout();
                            var savedOsc = Settings.Load(settingsPath);
                            if (savedOsc.OscTimeoutInt != 42 || savedOsc.OscTimeoutFloat != 0.75f || oscTimeoutInt.Text != "42" ||
                                oscTimeoutFloat.Text != 0.75f.ToString(System.Globalization.CultureInfo.CurrentCulture))
                                throw new InvalidOperationException("Custom fallback values must persist after saving.");
                            if (!savedOsc.OscZeroOnTimeout || savedOsc.OscTimeoutSeconds != 12 || oscTimeoutMode.SelectedIndex != 1 || oscTimeoutSeconds.Text != "12")
                                throw new InvalidOperationException("OSC timeout mode and interval must persist after saving.");
                            capture($"smoke-{language}-{mode.ToLowerInvariant()}-osc-timeout");
                            oscTimeoutSeconds.Text = "0";
                            SaveClicked(null, null);
                            if (Settings.Load(settingsPath).OscTimeoutSeconds != 12 || settings.OscTimeoutSeconds != 12)
                                throw new InvalidOperationException("An invalid OSC timeout must not overwrite saved settings.");
                            oscTimeoutSeconds.Text = "12";
                            foreach (string invalid in new[] { "-1", "256", "invalid" }) {
                                oscTimeoutInt.Text = invalid; SaveClicked(null, null);
                                if (Settings.Load(settingsPath).OscTimeoutInt != 42 || settings.OscTimeoutInt != 42)
                                    throw new InvalidOperationException("Invalid Int fallback must not overwrite saved settings.");
                            }
                            oscTimeoutInt.Text = "42";
                            foreach (string invalid in new[] { "-1", "2", "NaN", "Infinity", "invalid" }) {
                                oscTimeoutFloat.Text = invalid; SaveClicked(null, null);
                                if (Settings.Load(settingsPath).OscTimeoutFloat != 0.75f || settings.OscTimeoutFloat != 0.75f)
                                    throw new InvalidOperationException("Invalid Float fallback must not overwrite saved settings.");
                            }
                            oscTimeoutFloat.Text = 0.75f.ToString(System.Globalization.CultureInfo.CurrentCulture);
                            oscTimeoutMode.SelectedIndex = 0;
                            if (oscTimeoutSeconds.IsEnabled || oscTimeoutInt.IsEnabled || oscTimeoutFloat.IsEnabled) throw new InvalidOperationException("Keep-last mode must disable the interval and fallback fields.");
                            SaveClicked(null, null); await Task.Delay(80); UpdateLayout();
                            if (Settings.Load(settingsPath).OscZeroOnTimeout) throw new InvalidOperationException("Keep-last mode must persist.");
                            if (tabs.SelectedIndex != 3 || settingsCategory != 5 || !settingsSections[5].IsVisible || settingsLinks[5].IsChecked != true)
                                throw new InvalidOperationException("Saving settings lost the selected tab or category.");
                            File.AppendAllText(checksPath, $"PASS {language}/{mode}: category retained across tabs and save, OSC timeout and custom fallback values persist, invalid interval and fallback values rejected, fields enabled only in fallback mode.{Environment.NewLine}");
                            NavigateSettings(0);
                        }
                        if (i == 1) {
                            await RefreshActivity(true); UpdateLayout();
                            if (historyDateInput.ActualWidth < 150 || historyTimeInput.ActualWidth < 115 || activityCalendar.ActualWidth < 410)
                                throw new InvalidOperationException("Calendar and exact-date fields must retain readable layout widths.");
                            capture($"history-calendar-{language}-{mode.ToLowerInvariant()}");
                            await VerifySleepLabels();
                            capture($"sleep-labels-{language}-{mode.ToLowerInvariant()}");
                            if (analysisExportButton.IsEnabled)
                                throw new InvalidOperationException("Analysis export must be disabled without a selection.");
                            if (historyData != null) {
                                var original = historyWindow;
                                var originalWidth = historyWidth;
                                var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120) {
                                    RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent
                                };
                                historyChart.RaiseEvent(wheel);
                                if (historyWidth != originalWidth || historyWindow.From >= original.From)
                                    throw new InvalidOperationException("Wheel up must pan earlier without resizing.");
                                if (!wheel.Handled) throw new InvalidOperationException("History wheel input must not scroll the containing page.");
                                historyChart.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) {
                                    RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent
                                });
                                if (historyWindow.From != original.From || historyWindow.To != original.To)
                                    throw new InvalidOperationException("Wheel down must return to the latest window.");
                                historyChart.HandleWheel(0.5, 120, System.Windows.Input.ModifierKeys.Control);
                                if (historyWidth >= originalWidth)
                                    throw new InvalidOperationException("Ctrl+wheel must still zoom the history chart.");
                                historyWindow = original; historyWidth = originalWidth; ScheduleHistory(); await LoadHistory();
                                File.AppendAllText(checksPath, $"PASS {language}/{mode}: history wheel pans both directions and Ctrl+wheel zooms.{Environment.NewLine}");
                                var select = typeof(HeartChart).GetMethod("SetSelectionRange", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                                select.Invoke(historyChart, new object[] { new TimeWindow(historyWindow.From, historyWindow.From + Math.Max(1, historyWidth / 2)) });
                                if (!analysisExportButton.IsEnabled || !exportButton.IsEnabled)
                                    throw new InvalidOperationException("Both CSV actions must be available for a loaded selected interval.");
                                exportingHistory = true; UpdateExportButtons();
                                if (analysisExportButton.IsEnabled || exportButton.IsEnabled)
                                    throw new InvalidOperationException("Both CSV actions must be disabled during export.");
                                exportingHistory = false; UpdateExportButtons();
                                await Task.Delay(150); UpdateLayout();
                                capture($"smoke-{language}-{mode.ToLowerInvariant()}-analysis-export");
                                historyChart.ClearSelection();
                                if (analysisExportButton.IsEnabled || !exportButton.IsEnabled)
                                    throw new InvalidOperationException("Clearing analysis must disable only interval export.");
                                select.Invoke(historyChart, new object[] { new TimeWindow(historyWindow.From, historyWindow.From + 1) });
                                ScheduleHistory();
                                if (analysisExportButton.IsEnabled || exportButton.IsEnabled)
                                    throw new InvalidOperationException("Changing displayed range must clear analysis and disable CSV while loading.");
                                await LoadHistory();
                                File.AppendAllText(checksPath, $"PASS {language}/{mode}: analysis CSV selection, clearing, range change and export busy states.{Environment.NewLine}");
                            }
                            // Isolated smoke mode only: exercise the tightest layout
                            // with errors and a long export path.
                            errorText.Text = string.Concat(Enumerable.Repeat(T("検証用の接続エラー。", "Isolated connection-error verification. "), 12));
                            historyStats.Text = string.Concat(Enumerable.Repeat(T("検証用の読み込みエラー。", "Isolated storage-error verification. "), 12));
                            exportStatus.Text = T("検証用の出力先: ", "Isolated export-path verification: ") + new string('x', 240) + ".csv";
                            await Task.Delay(150); UpdateLayout();
                            capture($"smoke-{language}-{mode.ToLowerInvariant()}-history-constrained");
                            errorText.Text = ""; exportStatus.Text = "";
                        }
                    }
                }
                ExitApplication();
            };
        }

    }
}
