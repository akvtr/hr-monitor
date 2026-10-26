using System;

namespace HeartRateMonitor
{
    // Only successful, contiguous observations may extend an interval. Unknown gaps never do.
    internal sealed class SleepIntervalTracker
    {
        private readonly Store store;
        private SleepInterval current;
        private long lastObserved;
        internal SleepIntervalTracker(Store store) { this.store = store; }
        internal bool Observe(bool? asleep, long now)
        {
            if (!asleep.HasValue) { current = null; return false; }
            if (current != null && (now < lastObserved || now - lastObserved > 10000)) current = null;
            lastObserved = now;
            if (asleep.Value) {
                if (current == null) current = new SleepInterval { From = now, To = now };
                current.To = now;
                try { store.SaveSleepInterval(current); }
                catch { current = null; throw; }
                return true;
            }
            if (current == null) return false;
            current.To = now;
            try { store.SaveSleepInterval(current); }
            finally { current = null; }
            return true;
        }
    }
}
