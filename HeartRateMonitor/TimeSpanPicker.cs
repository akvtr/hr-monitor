using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace HeartRateMonitor
{
    // Logarithmic travel keeps short windows usable even with months of history.
    internal sealed class TimeSpanPicker : StackPanel
    {
        private readonly Slider slider;
        private readonly TextBlock value;
        private bool updating;
        public event Action<long> WidthChanged;

        public TimeSpanPicker(string id)
        {
            var row = new DockPanel();
            value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 118, TextAlignment = TextAlignment.Right };
            value.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            AutomationProperties.SetAutomationId(value, id + "Value");
            DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
            var label = new TextBlock { Text = Words.T("表示幅", "Time span"), VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted"); row.Children.Add(label);
            Children.Add(row);
            slider = new Slider { Minimum = 0, Maximum = 1, Height = 32, SmallChange = 0.05, LargeChange = 0.25, IsMoveToPointEnabled = true };
            slider.SetResourceReference(StyleProperty, "HistoryTimelineSlider");
            AutomationProperties.SetAutomationId(slider, id);
            Children.Add(slider);
            var help = new TextBlock { Text = Words.T("左で短く・右で長く（矢印キーでも調整）", "Shorter ← → Longer (arrow keys to adjust)"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            help.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted"); Children.Add(help);
            slider.ValueChanged += (s, e) => { if (!updating) WidthChanged?.Invoke(Decode(slider.Value, maximum)); };
        }
        private long maximum = 3600000;
        internal static double Encode(long width) => Math.Log(Math.Max(60000, width) / 60000.0, 2);
        internal static long Decode(double position, long maximum)
        {
            if (position >= Encode(maximum) - 1e-10) return maximum;
            // Whole seconds avoid floating-point drift at preset boundaries.
            return Math.Min(maximum, Math.Max(60000, (long)Math.Round(Math.Pow(2, position) * 60) * 1000));
        }
        public void Update(long width, long limit, bool enabled = true)
        {
            updating = true;
            maximum = Math.Max(60000, limit);
            slider.Maximum = Encode(maximum);
            slider.Value = Encode(Math.Min(width, maximum));
            slider.IsEnabled = enabled && maximum > 60000;
            var duration = TimeSpan.FromMilliseconds(width);
            value.Text = Words.T("表示中: ", "Showing: ") + (duration.TotalDays >= 1 ? ((int)duration.TotalDays) + Words.T("日 ", "d ") : "")
                + (duration.TotalHours >= 1 ? duration.Hours + Words.T("時間 ", "h ") : "")
                + duration.Minutes + Words.T("分", "m") + (duration.Seconds > 0 ? " " + duration.Seconds + Words.T("秒", "s") : "");
            AutomationProperties.SetName(slider, Words.T("表示幅の調整。", "Adjust time span. ") + value.Text);
            slider.ToolTip = value.Text;
            updating = false;
        }
    }
}
