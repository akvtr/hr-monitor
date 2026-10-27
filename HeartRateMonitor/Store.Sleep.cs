using System;
using System.Collections.Generic;

namespace HeartRateMonitor
{
    public sealed partial class Store
    {
        internal int PrunedSleepIntervals { get; private set; }
        internal TimeWindow HistoryBounds()
        {
            lock (gate) {
                var readings = Bounds();
                var s = Prepare("SELECT (SELECT from_ms FROM sleep_intervals WHERE to_ms>from_ms ORDER BY from_ms LIMIT 1),(SELECT to_ms FROM sleep_intervals WHERE to_ms>from_ms ORDER BY to_ms DESC LIMIT 1);");
                try {
                    int rc = Native.sqlite3_step(s); if (rc != Native.Row) Check(rc);
                    if (Native.sqlite3_column_type(s, 0) == Native.Null) return readings;
                    long first = Native.sqlite3_column_int64(s, 0), last = Native.sqlite3_column_int64(s, 1);
                    return new TimeWindow(Math.Min(readings?.From ?? first, first), Math.Max(readings?.To ?? last, last));
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        internal bool SaveSleepInterval(SleepInterval interval)
        {
            if (interval == null) throw new ArgumentNullException(nameof(interval));
            DateTimeOffset.FromUnixTimeMilliseconds(interval.From); DateTimeOffset.FromUnixTimeMilliseconds(interval.To);
            if (interval.To < interval.From) throw new ArgumentException("Sleep interval end must not precede start.");
            return ReadSnapshot(() => {
                var s = Prepare(interval.Id == 0 ? "INSERT INTO sleep_intervals(from_ms,to_ms) VALUES(?,?);"
                    : "UPDATE sleep_intervals SET from_ms=?,to_ms=? WHERE id=?;");
                try {
                    BindInt(s, 1, interval.From); BindInt(s, 2, interval.To);
                    if (interval.Id != 0) BindInt(s, 3, interval.Id);
                    int rc = Native.sqlite3_step(s); if (rc != Native.Done) Check(rc);
                    // A removed interval must never be resurrected by a stale tracker object.
                    if (interval.Id != 0 && Native.sqlite3_changes(db) == 0) {
                        return false;
                    }
                    if (interval.Id == 0) interval.Id = Native.sqlite3_last_insert_rowid(db);
                } finally { Native.sqlite3_finalize(s); }
                var state = Prepare("INSERT OR IGNORE INTO sleep_management(sleep_id,excluded,ongoing) VALUES(?,?,?);");
                try {
                    BindInt(state, 1, interval.Id); BindInt(state, 2, interval.Excluded ? 1 : 0); BindInt(state, 3, interval.IsOngoing ? 1 : 0);
                    int rc = Native.sqlite3_step(state); if (rc != Native.Done) Check(rc);
                } finally { Native.sqlite3_finalize(state); }
                state = Prepare("UPDATE sleep_management SET ongoing=? WHERE sleep_id=?;");
                try {
                    BindInt(state, 1, interval.IsOngoing ? 1 : 0); BindInt(state, 2, interval.Id);
                    int rc = Native.sqlite3_step(state); if (rc != Native.Done) Check(rc);
                } finally { Native.sqlite3_finalize(state); }
                return true;
            });
        }
        public List<SleepInterval> SleepIntervals(long from, long to)
        {
            lock (gate) {
                EnsureSleepManagement();
                var result = new List<SleepInterval>();
                var s = Prepare("SELECT i.id,i.from_ms,i.to_ms,COALESCE(m.excluded,0),COALESCE(m.ongoing,0) FROM sleep_intervals i LEFT JOIN sleep_management m ON m.sleep_id=i.id WHERE i.from_ms<? AND i.to_ms>? AND i.to_ms>i.from_ms AND ?<? ORDER BY i.from_ms,i.id;");
                try {
                    BindInt(s, 1, to); BindInt(s, 2, from); BindInt(s, 3, from); BindInt(s, 4, to); int rc;
                    while ((rc = Native.sqlite3_step(s)) == Native.Row)
                        result.Add(new SleepInterval { Id = Native.sqlite3_column_int64(s, 0), From = Native.sqlite3_column_int64(s, 1), To = Native.sqlite3_column_int64(s, 2),
                            Excluded = Native.sqlite3_column_int64(s, 3) != 0, IsOngoing = Native.sqlite3_column_int64(s, 4) != 0 });
                    if (rc != Native.Done) Check(rc); return result;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
    }
}
