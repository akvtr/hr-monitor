namespace HeartRateMonitor
{
    public sealed partial class Store
    {
        public bool DeleteSleepInterval(long id)
        {
            lock (gate) {
                EnsureSleepManagement();
                // Check the persisted marker in the same statement as deletion. A stale UI
                // selection must never remove an interval still owned by the recorder.
                var s = Prepare("DELETE FROM sleep_intervals WHERE id=? AND NOT EXISTS(SELECT 1 FROM sleep_management WHERE sleep_id=? AND ongoing<>0);");
                try {
                    BindInt(s, 1, id); BindInt(s, 2, id);
                    int rc = Native.sqlite3_step(s); if (rc != Native.Done) Check(rc);
                    // The cleanup trigger removes only this interval's management metadata.
                    return Native.sqlite3_changes(db) != 0;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
    }
}
