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
        internal void SaveSleepInterval(SleepInterval interval)
        {
            if (interval == null) throw new ArgumentNullException(nameof(interval));
            DateTimeOffset.FromUnixTimeMilliseconds(interval.From); DateTimeOffset.FromUnixTimeMilliseconds(interval.To);
            if (interval.To < interval.From) throw new ArgumentException("Sleep interval end must not precede start.");
            lock (gate) {
                var s = Prepare(interval.Id == 0 ? "INSERT INTO sleep_intervals(from_ms,to_ms) VALUES(?,?);"
                    : "UPDATE sleep_intervals SET from_ms=?,to_ms=? WHERE id=?;");
                try {
                    BindInt(s, 1, interval.From); BindInt(s, 2, interval.To);
                    if (interval.Id != 0) BindInt(s, 3, interval.Id);
                    int rc = Native.sqlite3_step(s); if (rc != Native.Done) Check(rc);
                    // Retention may have removed an old active interval while observation was suspended.
                    if (interval.Id != 0 && Native.sqlite3_changes(db) == 0) {
                        interval.Id = 0; SaveSleepInterval(interval); return;
                    }
                    if (interval.Id == 0) interval.Id = Native.sqlite3_last_insert_rowid(db);
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public List<SleepInterval> SleepIntervals(long from, long to)
        {
            lock (gate) {
                var result = new List<SleepInterval>();
                var s = Prepare("SELECT id,from_ms,to_ms FROM sleep_intervals WHERE from_ms<? AND to_ms>? AND to_ms>from_ms AND ?<? ORDER BY from_ms,id;");
                try {
                    BindInt(s, 1, to); BindInt(s, 2, from); BindInt(s, 3, from); BindInt(s, 4, to); int rc;
                    while ((rc = Native.sqlite3_step(s)) == Native.Row)
                        result.Add(new SleepInterval { Id = Native.sqlite3_column_int64(s, 0), From = Native.sqlite3_column_int64(s, 1), To = Native.sqlite3_column_int64(s, 2) });
                    if (rc != Native.Done) Check(rc); return result;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
    }
}
