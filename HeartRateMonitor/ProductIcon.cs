using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HeartRateMonitor
{
    internal static class ProductIcon
    {
        private const string ResourceName = "HeartRateMonitor.AppIcon.ico";
        private static Stream OpenResource()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null) throw new InvalidOperationException("The application icon resource is missing.");
            return stream;
        }
        internal static ImageSource CreateWindowIcon()
        {
            using (var stream = OpenResource()) {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (frame.CanFreeze) frame.Freeze();
                return frame;
            }
        }
        internal static Icon CreateTrayIcon()
        {
            using (var stream = OpenResource())
            using (var source = new Icon(stream)) return (Icon)source.Clone();
        }
    }
}
