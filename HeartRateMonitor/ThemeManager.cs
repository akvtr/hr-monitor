using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace HeartRateMonitor
{
    public static class ThemeManager
    {
        public static void Initialize(Window window)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HeartRateMonitor.Theme.xaml"))
                window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        public static bool IsDark(string mode)
        {
            if (mode != "System") return mode == "Dark";
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return (int)(key?.GetValue("AppsUseLightTheme", 1) ?? 1) == 0; }
            catch { return false; }
        }
        public static bool CanAnimate => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        public static void Apply(Window window, string mode, bool animate = false, Action completed = null)
        {
            bool highContrast = SystemParameters.HighContrast;
            bool dark = !highContrast && IsDark(mode);
            var colors = new Dictionary<string, string[]> {
                { "Activity0", new[] { "#C5D4D0", "#4B5056" } },
                { "Activity1", new[] { "#A0C9C0", "#3E536F" } },
                { "Activity2", new[] { "#6AA99B", "#597EA9" } },
                { "Activity3", new[] { "#3B8979", "#7CA4D4" } },
                { "Activity4", new[] { "#167868", "#AACBFA" } },
                { "Scrim", new[] { "#66000000", "#99000000" } },
                { "Background", new[] { "#F1F6F5", "#161616" } },
                { "Surface", new[] { "#FFFFFF", "#202020" } },
                { "SurfaceRaised", new[] { "#FFFFFF", "#252525" } },
                { "SurfaceAlt", new[] { "#F5F9F8", "#2B2B2B" } },
                { "Input", new[] { "#FFFFFF", "#181818" } },
                { "Text", new[] { "#142C31", "#F0F0F0" } },
                { "TextMuted", new[] { "#53686D", "#B5B5B5" } },
                { "Line", new[] { "#D9E5E2", "#3D3D3D" } },
                { "ChartGrid", new[] { "#E0EAE7", "#353535" } },
                { "Hover", new[] { "#EDF5F2", "#333333" } },
                { "Focus", new[] { "#0D6F61", "#9EC5FF" } },
                { "Accent", new[] { "#167868", "#8AB4F8" } },
                { "AccentSoft", new[] { "#E5F4EF", "#27364A" } },
                { "AccentInk", new[] { "#FFFFFF", "#101C2F" } },
                { "Success", new[] { "#176D49", "#8BCC9B" } },
                { "SuccessSoft", new[] { "#E2F5EC", "#26392C" } },
                { "Warning", new[] { "#825000", "#F0C36C" } },
                { "WarningSoft", new[] { "#FFF4D6", "#48391C" } },
                { "Danger", new[] { "#B04439", "#FF9588" } },
                { "DangerSoft", new[] { "#FCE9E7", "#452522" } }
            };
            if (highContrast) {
                var system = new Dictionary<string, Brush> {
                    { "Scrim", SystemColors.WindowBrush }, { "Background", SystemColors.WindowBrush }, { "Surface", SystemColors.WindowBrush }, { "SurfaceRaised", SystemColors.WindowBrush },
                    { "SurfaceAlt", SystemColors.ControlBrush }, { "Input", SystemColors.WindowBrush }, { "Text", SystemColors.WindowTextBrush },
                    { "TextMuted", SystemColors.ControlTextBrush }, { "Line", SystemColors.WindowTextBrush }, { "ChartGrid", SystemColors.GrayTextBrush },
                    { "Hover", SystemColors.ControlBrush }, { "Focus", SystemColors.HighlightBrush }, { "Accent", SystemColors.HighlightBrush },
                    { "AccentSoft", SystemColors.ControlBrush }, { "AccentInk", SystemColors.HighlightTextBrush },
                    { "Success", SystemColors.WindowTextBrush }, { "SuccessSoft", SystemColors.ControlBrush },
                    { "Warning", SystemColors.WindowTextBrush }, { "WarningSoft", SystemColors.ControlBrush },
                    { "Danger", SystemColors.WindowTextBrush }, { "DangerSoft", SystemColors.ControlBrush }
                };
                foreach (var pair in system) window.Resources[pair.Key] = pair.Value;
                for (int i = 0; i <= 4; i++) window.Resources["Activity" + i] = i == 0 ? SystemColors.ControlBrush : SystemColors.HighlightBrush;
            } else foreach (var pair in colors) {
                    var target = (Color)ColorConverter.ConvertFromString(pair.Value[dark ? 1 : 0]);
                    var previous = window.Resources[pair.Key] as SolidColorBrush;
                    var from = previous == null ? target : previous.Color;
                    // Stop interrupted clocks. Only small shared brushes animate; no screen captures.
                    if (previous != null && !previous.IsFrozen) previous.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    var brush = new SolidColorBrush(target);
                    if (animate && CanAnimate && from != target) {
                        string key = pair.Key;
                        var transition = new ColorAnimation(from, target, TimeSpan.FromMilliseconds(360)) {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop
                        };
                        transition.Completed += (s, e) => {
                            if (!ReferenceEquals(window.Resources[key], brush)) return;
                            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                            brush.Freeze();
                            // Resource replacement refreshes charts whose pens use frozen snapshots.
                            window.Resources[key] = brush.CloneCurrentValue().GetAsFrozen();
                            if (key == "Background") completed?.Invoke();
                        };
                        brush.BeginAnimation(SolidColorBrush.ColorProperty, transition);
                        // Start before publishing: WPF can freeze a resource when a style first uses it.
                        window.Resources[key] = brush;
                    } else { brush.Freeze(); window.Resources[pair.Key] = brush; }
            }
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) {
                int flag = dark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, 20, ref flag, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref flag, sizeof(int));
            }
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
