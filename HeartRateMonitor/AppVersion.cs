using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("HeartRateMonitor.Tests")]

[assembly: AssemblyTitle("Heart Rate Monitor")]
[assembly: AssemblyProduct("Heart Rate Monitor")]
[assembly: AssemblyVersion(HeartRateMonitor.AppVersion.Assembly)]
[assembly: AssemblyFileVersion(HeartRateMonitor.AppVersion.Assembly)]
[assembly: AssemblyInformationalVersion(HeartRateMonitor.AppVersion.Product)]

namespace HeartRateMonitor
{
    internal static class AppVersion
    {
        internal const string Product = "0.23.0";
        internal const string Assembly = "0.23.0.0";
    }
}
