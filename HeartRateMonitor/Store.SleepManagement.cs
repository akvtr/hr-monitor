using System;
using System.Linq;

namespace HeartRateMonitor
{
    public sealed partial class Store
    {
        private bool sleepManagementReady;
        private void EnsureSleepManagement()
        {
            if (sleepManagementReady) return;
            Execute("CREATE TABLE IF NOT EXISTS sleep_management(sleep_id INTEGER PRIMARY KEY,excluded INTEGER NOT NULL DEFAULT 0,ongoing INTEGER NOT NULL DEFAULT 0);");
            Execute("CREATE TRIGGER IF NOT EXISTS sleep_management_cleanup AFTER DELETE ON sleep_intervals BEGIN DELETE FROM sleep_management WHERE sleep_id=OLD.id; END;");
            sleepManagementReady = true;
        }
        // Only the recording owner calls this at startup or when replacing its tracker.
        public void CloseStaleSleepIntervals()
        {
            lock (gate) { EnsureSleepManagement(); Execute("UPDATE sleep_management SET ongoing=0 WHERE ongoing<>0;"); }
        }
        public bool SetSleepExcluded(long id, bool excluded)
        {
            lock (gate) {
                EnsureSleepManagement();
                var s = Prepare("INSERT OR REPLACE INTO sleep_management(sleep_id,excluded,ongoing) SELECT id,?,0 FROM sleep_intervals WHERE id=? AND NOT EXISTS(SELECT 1 FROM sleep_management WHERE sleep_id=? AND ongoing<>0);");
                try {
                    BindInt(s, 1, excluded ? 1 : 0); BindInt(s, 2, id); BindInt(s, 3, id);
                    int rc = Native.sqlite3_step(s); if (rc != Native.Done) Check(rc);
                    return Native.sqlite3_changes(db) != 0;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        // Boundaries are converted separately, preserving actual elapsed time through DST changes.
        public static long SleepDayBoundary(DateTime day, TimeZoneInfo zone, int boundaryHour)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (boundaryHour < 0 || boundaryHour > 23) throw new ArgumentOutOfRangeException(nameof(boundaryHour));
            DateTime local = DateTime.SpecifyKind(day.Date.AddHours(boundaryHour), DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUnixTimeMilliseconds();
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)).ToUnixTimeMilliseconds();
        }
    }
}
