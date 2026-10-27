using System;

namespace HeartRateMonitor
{
    // Only successful, contiguous observations may extend an interval. Unknown gaps never do.
    internal sealed class SleepIntervalTracker
    {
        private readonly Store store;
        private SleepInterval current;
        private long lastObserved;
        internal SleepIntervalTracker(Store store) { this.store = store; store.CloseStaleSleepIntervals(); }
        internal void Close()
        {
            if (current == null) return;
            try { current.IsOngoing = false; store.SaveSleepInterval(current); }
            finally { current = null; }
        }
        internal bool Observe(bool? asleep, long now)
        {
            if (!asleep.HasValue) { Close(); return false; }
            bool closed = current != null && (now < lastObserved || now - lastObserved > 10000);
            if (closed) Close();
            lastObserved = now;
            if (asleep.Value) {
                if (current == null) current = new SleepInterval { From = now, To = now, IsOngoing = true };
                current.To = now;
                try {
                    if (!store.SaveSleepInterval(current)) {
                        // Retention may have pruned a long-stale active row. Resume at this
                        // confirmed observation, never by restoring its expired start time.
                        current = new SleepInterval { From = now, To = now, IsOngoing = true };
                        store.SaveSleepInterval(current);
                    }
                }
                catch { current = null; throw; }
                return true;
            }
            if (current == null) return closed;
            current.To = now;
            current.IsOngoing = false;
            try { store.SaveSleepInterval(current); }
            finally { current = null; }
            return true;
        }
    }
}
