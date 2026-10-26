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
                await Task.Delay(300);
                string checksPath = Path.Combine(Path.GetDirectoryName(path), "smoke-checks.txt");
                File.WriteAllText(checksPath, "");
                foreach (var language in new[] { "ja", "en" }) foreach (var mode in new[] { "Dark", "Light", "System" }) {
                    settings.Language = language; settings.Theme = mode; settings.Save(settingsPath);
                    Words.Language = language; BuildUi(); ApplyTheme();
                    for (int i = 0; i < 3; i++) {
                        tabs.SelectedIndex = i;
                        if (i == 1) await RefreshHistory(true);
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
                        if (i == 2) {
                            if (tabs.Items.Count != 3 || tabs.SelectedIndex != 2)
                                throw new InvalidOperationException("Settings must remain the third tab.");
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
                            var selected = (TabItem)tabs.Items[2]; selected.ApplyTemplate();
                            var selectedShell = (Border)selected.Template.FindName("Shell", selected);
                            var selectedLine = (Border)selected.Template.FindName("SelectedLine", selected);
                            if (!ReferenceEquals(selectedShell.Background, FindResource("AccentSoft")) || !ReferenceEquals(selected.Foreground, FindResource("Text")) || selectedLine.Visibility != Visibility.Visible)
                                throw new InvalidOperationException("Selected tab must have a soft fill, readable text and a selection line.");
                            NavigateSettings(3);
                            host.Text = "127.0.0.2";
                            tabs.SelectedIndex = 0; await Task.Delay(50); tabs.SelectedIndex = 2; await Task.Delay(80); UpdateLayout();
                            if (host.Text != "127.0.0.2" || settingsCategory != 3 || !settingsSections[3].IsVisible)
                                throw new InvalidOperationException("Switching tabs lost unsaved settings or the selected category.");
                            host.Text = previousHost;
                            NavigateSettings(5);
                            oscTimeoutMode.SelectedIndex = 1;
                            oscTimeoutSeconds.Text = "12";
                            if (!oscTimeoutSeconds.IsEnabled) throw new InvalidOperationException("Zero-on-timeout mode must enable its interval field.");
                            SaveClicked(null, null); await Task.Delay(200); UpdateLayout();
                            var savedOsc = Settings.Load(settingsPath);
                            if (!savedOsc.OscZeroOnTimeout || savedOsc.OscTimeoutSeconds != 12 || oscTimeoutMode.SelectedIndex != 1 || oscTimeoutSeconds.Text != "12")
                                throw new InvalidOperationException("OSC timeout mode and interval must persist after saving.");
                            capture($"smoke-{language}-{mode.ToLowerInvariant()}-osc-timeout");
                            oscTimeoutSeconds.Text = "0";
                            SaveClicked(null, null);
                            if (Settings.Load(settingsPath).OscTimeoutSeconds != 12 || settings.OscTimeoutSeconds != 12)
                                throw new InvalidOperationException("An invalid OSC timeout must not overwrite saved settings.");
                            oscTimeoutSeconds.Text = "12";
                            oscTimeoutMode.SelectedIndex = 0;
                            if (oscTimeoutSeconds.IsEnabled) throw new InvalidOperationException("Keep-last mode must disable the interval field.");
                            SaveClicked(null, null); await Task.Delay(80); UpdateLayout();
                            if (Settings.Load(settingsPath).OscZeroOnTimeout) throw new InvalidOperationException("Keep-last mode must persist.");
                            if (tabs.SelectedIndex != 2 || settingsCategory != 5 || !settingsSections[5].IsVisible || settingsLinks[5].IsChecked != true)
                                throw new InvalidOperationException("Saving settings lost the selected tab or category.");
                            File.AppendAllText(checksPath, $"PASS {language}/{mode}: category retained across tabs and save, OSC timeout persists, invalid interval rejected, interval enabled only in zero mode.{Environment.NewLine}");
                            NavigateSettings(0);
                        }
                        if (i == 1) {
                            await VerifySleepLabels();
                            capture($"sleep-labels-{language}-{mode.ToLowerInvariant()}");
                            if (analysisExportButton.IsEnabled)
                                throw new InvalidOperationException("Analysis export must be disabled without a selection.");
                            if (historyData != null) {
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
