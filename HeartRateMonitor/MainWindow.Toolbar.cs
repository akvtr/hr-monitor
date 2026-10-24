using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;

namespace HeartRateMonitor
{
    public sealed partial class MainWindow
    {
        private Button maximizeButton;
        private Path maximizeIcon;
        private bool toolbarDragArmed;
        private Point toolbarDragOrigin;
        private void InitializeWindowChrome()
        {
            WindowStyle = WindowStyle.None;
            // Register before WindowChrome so its generic client hit test cannot consume our caption region.
            SourceInitialized += (s, e) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WindowBoundsHook);
            WindowChrome.SetWindowChrome(this, new WindowChrome {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(6, 2, 6, 6),
                GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            });
        }
        private IntPtr WindowBoundsHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0084) { // WM_NCHITTEST: physical screen coordinates, including negative monitor origins.
                long coordinates = lParam.ToInt64();
                var point = PointFromScreen(new Point((short)(coordinates & 0xffff), (short)((coordinates >> 16) & 0xffff)));
                double edge = WindowState == WindowState.Maximized ? 0 : 2;
                if (point.Y >= edge && point.Y < 36 && point.X >= 6 && point.X < ActualWidth - 6) {
                    // Route the whole toolbar to WPF. Its own mouse handler calls DragMove;
                    // a caption hit-test result alone did not reliably start a move on user desktops.
                    handled = true; return new IntPtr(1); // HTCLIENT
                }
            }
            if (message != 0x0024) return IntPtr.Zero; // WM_GETMINMAXINFO
            var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return IntPtr.Zero;
            var limits = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
            limits.MaxPosition.X = monitor.Work.Left - monitor.Monitor.Left;
            limits.MaxPosition.Y = monitor.Work.Top - monitor.Monitor.Top;
            limits.MaxSize.X = monitor.Work.Right - monitor.Work.Left;
            limits.MaxSize.Y = monitor.Work.Bottom - monitor.Work.Top;
            var target = HwndSource.FromHwnd(hwnd)?.CompositionTarget;
            var scale = target == null ? Matrix.Identity : target.TransformToDevice;
            limits.MinTrackSize.X = Math.Max(limits.MinTrackSize.X, (int)Math.Ceiling(MinWidth * scale.M11));
            limits.MinTrackSize.Y = Math.Max(limits.MinTrackSize.Y, (int)Math.Ceiling(MinHeight * scale.M22));
            Marshal.StructureToPtr(limits, lParam, false); handled = true;
            return IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        private UIElement CreateWindowToolbar()
        {
            var layout = new Grid { Height = 36 };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Label("Heart Rate Monitor " + AppVersion.Product, 12, "TextMuted");
            title.Margin = new Thickness(12, 0, 0, 0); title.TextWrapping = TextWrapping.NoWrap;
            layout.Children.Add(title);
            var theme = CreateThemeButton(); WindowChrome.SetIsHitTestVisibleInChrome(theme, true);
            Grid.SetColumn(theme, 1); layout.Children.Add(theme);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(WindowButton("M2,7 L12,7", T("最小化", "Minimize"), "MinimizeWindow", (s, e) => WindowState = WindowState.Minimized));
            maximizeButton = WindowButton("M2,2 L12,2 12,12 2,12 Z", "", "MaximizeWindow", (s, e) => {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            });
            maximizeIcon = (Path)maximizeButton.Content; controls.Children.Add(maximizeButton);
            var close = WindowButton("M2,2 L12,12 M12,2 L2,12", T("閉じる", "Close"), "CloseWindow", (s, e) => Close());
            controls.Children.Add(close); Grid.SetColumn(controls, 3); layout.Children.Add(controls);
            UpdateWindowControls();
            var bar = new Border { Child = layout, BorderThickness = new Thickness(0, 0, 0, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, "Surface"); bar.SetResourceReference(Border.BorderBrushProperty, "Line");
            WindowChrome.SetIsHitTestVisibleInChrome(bar, true);
            ConfigureToolbarDrag(bar);
            AutomationProperties.SetAutomationId(bar, "WindowToolbar");
            return bar;
        }
        private void FormatToolbarAction(Button button)
        {
            button.Width = 36; button.Height = 32; button.VerticalAlignment = VerticalAlignment.Center;
            var icon = (Path)button.Content; icon.Width = icon.Height = 16; icon.StrokeThickness = 1.5;
            icon.StrokeStartLineCap = icon.StrokeEndLineCap = PenLineCap.Round; icon.StrokeLineJoin = PenLineJoin.Round;
            icon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
        }
        private void ConfigureToolbarDrag(Border bar)
        {
            bar.PreviewMouseLeftButtonDown += (s, e) => {
                DependencyObject hit = e.OriginalSource as DependencyObject;
                while (hit != null && hit != bar) {
                    if (hit is ButtonBase) return;
                    hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
                }
                if (e.ClickCount == 2) {
                    toolbarDragArmed = false; bar.ReleaseMouseCapture();
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                } else { toolbarDragOrigin = e.GetPosition(this); toolbarDragArmed = true; bar.CaptureMouse(); }
                e.Handled = true;
            };
            bar.MouseMove += (s, e) => {
                if (!toolbarDragArmed || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                var pointer = e.GetPosition(this);
                if (Math.Abs(pointer.X - toolbarDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(pointer.Y - toolbarDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                toolbarDragArmed = false; bar.ReleaseMouseCapture();
                if (WindowState == WindowState.Maximized) {
                    var screen = PointToScreen(pointer);
                    var source = PresentationSource.FromVisual(this); var scale = source.CompositionTarget.TransformToDevice;
                    double fraction = Math.Max(0, Math.Min(1, pointer.X / ActualWidth)), width = RestoreBounds.Width;
                    WindowState = WindowState.Normal;
                    Left = screen.X / scale.M11 - width * fraction; Top = screen.Y / scale.M22 - Math.Min(30, toolbarDragOrigin.Y);
                }
                try { DragMove(); }
                catch (InvalidOperationException ex) { errorText.Text = T("ウィンドウを移動できません: ", "Could not move window: ") + ex.Message; }
                e.Handled = true;
            };
            bar.MouseLeftButtonUp += (s, e) => { if (toolbarDragArmed) { toolbarDragArmed = false; bar.ReleaseMouseCapture(); e.Handled = true; } };
            bar.LostMouseCapture += (s, e) => toolbarDragArmed = false;
        }
        private Button WindowButton(string geometry, string label, string id, RoutedEventHandler handler)
        {
            var icon = new Path { Data = Geometry.Parse(geometry), Width = 12, Height = 12, Stretch = Stretch.Uniform, StrokeThickness = 1 };
            var button = new Button { Content = icon, Width = 46, Height = 36, Style = (Style)FindResource("ToolbarButton"), ToolTip = label };
            // The icon follows its button's themed hover/foreground state.
            icon.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true); button.Click += handler;
            return button;
        }
        private void UpdateWindowControls()
        {
            if (maximizeButton == null) return;
            bool maximized = WindowState == WindowState.Maximized;
            string label = maximized ? T("元に戻す", "Restore") : T("最大化", "Maximize");
            maximizeButton.ToolTip = label; AutomationProperties.SetName(maximizeButton, label);
            maximizeIcon.Data = Geometry.Parse(maximized ? "M2,5 L9,5 9,12 2,12 Z M5,5 L5,2 12,2 12,9 9,9" : "M2,2 L12,2 12,12 2,12 Z");
        }
    }
}
