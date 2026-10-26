using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private FrameworkElement[] settingsSections;
        private RadioButton[] settingsLinks;
        private bool updatingSettingsNavigation;
        private int settingsCategory;

        private UIElement CreateSettingsNavigation()
        {
            var titles = new[] {
                T("一般", "General"), T("心拍センサー", "Heart-rate sensor"),
                T("データ保存", "Data storage"), T("OyasumiVR連携", "OyasumiVR integration"),
                T("送信先とパラメーター", "Destination & parameters"), T("送信値と無受信時の動作", "Values & timeout behavior"),
                T("送信状況とテスト", "Transmission & test")
            };
            var items = new StackPanel();
            settingsLinks = new RadioButton[titles.Length];
            for (int i = 0; i < titles.Length; i++) {
                int index = i;
                if (i == 4) {
                    var group = Eyebrow(T("OSC送信", "OSC transmission")); group.Margin = new Thickness(12, 22, 0, 8); items.Children.Add(group);
                }
                var link = new RadioButton {
                    Content = new TextBlock { Text = i == 5 ? T("送信値と\n無受信時の動作", titles[i]) : titles[i], TextWrapping = TextWrapping.Wrap },
                    GroupName = "SettingsCategories", Style = (Style)FindResource("SettingsNavigationItem"), IsChecked = i == settingsCategory
                };
                AutomationProperties.SetAutomationId(link, "SettingsCategory" + i);
                AutomationProperties.SetName(link, titles[i]);
                link.Click += (s, e) => NavigateSettings(index);
                link.Checked += (s, e) => { if (!updatingSettingsNavigation) NavigateSettings(index); };
                settingsLinks[i] = link; items.Children.Add(link);
            }
            var navigation = new Border { Child = items, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 0, 12, 0), Margin = new Thickness(0, 0, 16, 0) };
            navigation.SetResourceReference(Border.BorderBrushProperty, "Line");
            AutomationProperties.SetAutomationId(navigation, "SettingsCategories");
            AutomationProperties.SetName(navigation, T("設定カテゴリ", "Settings categories"));
            return navigation;
        }

        private void NavigateSettings(int index)
        {
            settingsCategory = index;
            for (int section = 0; section < settingsSections.Length; section++)
                settingsSections[section].Visibility = section == index ? Visibility.Visible : Visibility.Collapsed;
            updatingSettingsNavigation = true;
            try { settingsLinks[index].IsChecked = true; }
            finally { updatingSettingsNavigation = false; }
        }
    }
}
