using System;
using System.Diagnostics;
using System.Threading;

namespace HeartRateMonitor
{
    internal static class InstanceNotification
    {
        internal static string EventName(string key) => "Local\\HeartRateMonitor-Show-" + key;

        internal static bool TryRestore(string key, int timeoutMilliseconds = 1000)
        {
            if (timeoutMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            // Another process can observe the mutex before the primary publishes its restore event.
            var elapsed = Stopwatch.StartNew();
            while (true) {
                try {
                    using (var signal = EventWaitHandle.OpenExisting(EventName(key))) return signal.Set();
                } catch (WaitHandleCannotBeOpenedException) {
                    long remaining = timeoutMilliseconds - elapsed.ElapsedMilliseconds;
                    if (remaining <= 0) return false;
                    Thread.Sleep((int)Math.Min(25, remaining));
                }
            }
        }
    }
}
