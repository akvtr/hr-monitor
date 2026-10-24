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
                T("外観と言語", "Appearance & language"), T("接続と常駐", "Connection & tray"),
                T("データ保存", "Data storage"), T("OSC送信先", "OSC destination"),
                T("心拍値の変換", "Value conversion"), T("切断・無受信時", "Receive timeout"),
                T("送信状況とテスト", "Transmission & test")
            };
            var items = new StackPanel();
            var title = Heading(T("カテゴリ", "Categories")); title.FontSize = 14;
            title.Margin = new Thickness(12, 2, 0, 14); items.Children.Add(title);
            settingsLinks = new RadioButton[titles.Length];
            for (int i = 0; i < titles.Length; i++) {
                int index = i;
                if (i == 3) {
                    var group = Eyebrow("OSC"); group.Margin = new Thickness(12, 22, 0, 8); items.Children.Add(group);
                }
                var link = new RadioButton {
                    Content = new TextBlock { Text = titles[i], TextWrapping = TextWrapping.Wrap },
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
