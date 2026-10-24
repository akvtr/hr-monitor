using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private Button themeToggle;
        private Path themeIcon;
        private RotateTransform themeRotation;
        private Button CreateThemeButton()
        {
            themeRotation = new RotateTransform();
            themeIcon = new Path { Width = 22, Height = 22, Stretch = Stretch.Uniform,
                StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = themeRotation };
            themeIcon.SetResourceReference(Shape.StrokeProperty, "Text");
            themeIcon.Width = themeIcon.Height = 16;
            themeToggle = new Button { Content = themeIcon, Width = 36, Height = 32,
                Style = (Style)FindResource("ToolbarButton"),
                Padding = new Thickness(8), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(themeToggle, "ToggleTheme");
            FormatToolbarAction(themeToggle);
            themeToggle.Click += ToggleThemeClicked;
            UpdateThemeIcon(false);
            return themeToggle;
        }
        private void ToggleThemeClicked(object sender, RoutedEventArgs e)
        {
            try {
                var next = settings.Copy();
                next.Theme = ThemeManager.IsDark(settings.Theme) ? "Light" : "Dark";
                // Persist only the theme; preserve unapplied edits in the Settings page.
                next.Save(settingsPath); settings = next;
                themeChoice.SelectedIndex = next.Theme == "Dark" ? 1 : 2;
                ApplyThemeTransition(true);
            } catch (Exception ex) { errorText.Text = T("テーマを保存できません: ", "Could not save theme: ") + ex.Message; }
        }
        private void UpdateThemeIcon(bool animate)
        {
            if (themeToggle == null) return;
            bool dark = ThemeManager.IsDark(settings.Theme);
            string label = dark ? T("ライトモードに切り替え", "Switch to light mode") : T("ダークモードに切り替え", "Switch to dark mode");
            themeToggle.ToolTip = label; AutomationProperties.SetName(themeToggle, label);
            // Show the destination: sun in dark mode, moon in light mode.
            themeIcon.Data = Geometry.Parse(dark
                ? "M12,7 A5,5 0 1 1 12,17 A5,5 0 1 1 12,7 M12,1 L12,3 M12,21 L12,23 M1,12 L3,12 M21,12 L23,12 M4.2,4.2 L5.6,5.6 M18.4,18.4 L19.8,19.8 M4.2,19.8 L5.6,18.4 M18.4,5.6 L19.8,4.2"
                : "M20.5,14.5 A9,9 0 1 1 9.5,3.5 A7,7 0 0 0 20.5,14.5");
            themeRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            if (!animate || !ThemeManager.CanAnimate) return;
            var turn = new DoubleAnimation(dark ? -90 : 90, 0, TimeSpan.FromMilliseconds(360)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            };
            turn.Completed += (s, e) => themeRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            themeRotation.BeginAnimation(RotateTransform.AngleProperty, turn);
        }
    }
}
