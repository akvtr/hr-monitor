using System;
using System.Collections.Generic;
using System.IO;

namespace HeartRateMonitor
{
    // Windows 10+ ships the SQLite C API in winsqlite3.dll. All parameters are bound.
    public sealed partial class Store : IDisposable
    {
        private const string ValidReadingCondition = "bpm>0 AND (contact IS NULL OR contact=1)";
        private readonly string databasePath;
        private readonly object gate = new object();
        public Store(string path)
        {
            databasePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            try {
                OpenDatabase();
                Execute("PRAGMA journal_mode=WAL;");
                Execute("PRAGMA synchronous=FULL;");
                Execute("CREATE TABLE IF NOT EXISTS readings (id INTEGER PRIMARY KEY AUTOINCREMENT, utc_ms INTEGER NOT NULL, session TEXT NOT NULL, device TEXT NOT NULL, bpm INTEGER NOT NULL, contact INTEGER, rr_ms TEXT NOT NULL, energy INTEGER);");
                Execute("CREATE INDEX IF NOT EXISTS readings_time ON readings(utc_ms);");
                Execute("CREATE TABLE IF NOT EXISTS sleep_intervals (id INTEGER PRIMARY KEY AUTOINCREMENT, from_ms INTEGER NOT NULL, to_ms INTEGER NOT NULL CHECK(to_ms>=from_ms));");
                Execute("CREATE INDEX IF NOT EXISTS sleep_intervals_end ON sleep_intervals(to_ms);");
            } catch { Dispose(); throw; }
        }
        // Keep related history queries consistent while other connections record or prune data.
        internal T ReadSnapshot<T>(Func<T> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            lock (gate) {
                Execute("BEGIN;");
                try {
                    T result = read();
                    Execute("COMMIT;");
                    return result;
                } catch (Exception readError) {
                    try { RollbackActiveTransaction(); }
                    catch (Exception rollbackError) {
                        throw new AggregateException(Words.T("履歴読み込みとロールバックに失敗しました。", "History read and rollback both failed."), readError, rollbackError);
                    }
                    throw;
                }
            }
        }
        public void Add(Reading row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            lock (gate) {
                var s = Prepare("INSERT INTO readings(utc_ms,session,device,bpm,contact,rr_ms,energy) VALUES(?,?,?,?,?,?,?);");
                try {
                    BindInt(s, 1, row.UnixMs); BindText(s, 2, row.Session); BindText(s, 3, row.Device); BindInt(s, 4, row.Bpm);
                    if (row.Contact.HasValue) BindInt(s, 5, row.Contact.Value ? 1 : 0);
                    BindText(s, 6, row.Rr); if (row.Energy.HasValue) BindInt(s, 7, row.Energy.Value);
                    int rc = Native.sqlite3_step(s); if (rc != Native.Done) Check(rc);
                    row.Id = Native.sqlite3_last_insert_rowid(db);
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public int DeleteBefore(long utcMs)
        {
            return ReadSnapshot(() => {
                var prune = Prepare("DELETE FROM sleep_intervals WHERE to_ms<=?;");
                try {
                    BindInt(prune, 1, utcMs); int rc = Native.sqlite3_step(prune); if (rc != Native.Done) Check(rc);
                    PrunedSleepIntervals = Native.sqlite3_changes(db);
                }
                finally { Native.sqlite3_finalize(prune); }
                var s = Prepare("DELETE FROM readings WHERE utc_ms < ?;");
                try {
                    BindInt(s, 1, utcMs);
                    int rc = Native.sqlite3_step(s); if (rc != Native.Done) Check(rc);
                    return Native.sqlite3_changes(db);
                } finally { Native.sqlite3_finalize(s); }
            });
        }
        private Reading Row(IntPtr s) => new Reading {
            Id = Native.sqlite3_column_int64(s, 0), UnixMs = Native.sqlite3_column_int64(s, 1),
            Session = ColumnText(s, 2), Device = ColumnText(s, 3),
            Bpm = (int)Native.sqlite3_column_int64(s, 4),
            Contact = Native.sqlite3_column_type(s, 5) == Native.Null ? (bool?)null : Native.sqlite3_column_int64(s, 5) != 0,
            Rr = ColumnText(s, 6), Energy = Native.sqlite3_column_type(s, 7) == Native.Null ? (int?)null : (int)Native.sqlite3_column_int64(s, 7)
        };
        public List<Reading> Query(long from, long to, int limit = 1000)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
            lock (gate) {
                var result = new List<Reading>();
                var s = Prepare("SELECT id,utc_ms,session,device,bpm,contact,rr_ms,energy FROM readings WHERE utc_ms>=? AND utc_ms<? ORDER BY utc_ms DESC,id DESC LIMIT ?;");
                try { BindInt(s, 1, from); BindInt(s, 2, to); BindInt(s, 3, limit); int rc;
                    while ((rc = Native.sqlite3_step(s)) == Native.Row) result.Add(Row(s));
                    if (rc != Native.Done) Check(rc); return result;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public HistorySummary Summary(long from, long to)
        {
            lock (gate) {
                var s = Prepare("SELECT COUNT(*),MIN(bpm),AVG(bpm),MAX(bpm) FROM readings WHERE utc_ms>=? AND utc_ms<? AND " + ValidReadingCondition + ";");
                try { BindInt(s, 1, from); BindInt(s, 2, to); int rc = Native.sqlite3_step(s); if (rc != Native.Row) Check(rc);
                    return new HistorySummary { Count = Native.sqlite3_column_int64(s, 0), Min = (int)Native.sqlite3_column_int64(s, 1), Average = Native.sqlite3_column_double(s, 2), Max = (int)Native.sqlite3_column_int64(s, 3) };
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public List<DailyActivity> DailyActivity(DateTime firstDay, DateTime lastDay, TimeZoneInfo zone)
        {
            firstDay = firstDay.Date; lastDay = lastDay.Date;
            if (lastDay < firstDay || (lastDay - firstDay).Days > 366) throw new ArgumentOutOfRangeException(nameof(lastDay));
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            lock (gate) {
                var result = new List<DailyActivity>();
                var s = Prepare("SELECT COUNT(*) FROM readings WHERE utc_ms>=? AND utc_ms<? AND " + ValidReadingCondition + ";");
                try {
                    long from = HeartRateMonitor.DailyActivity.Midnight(firstDay, zone);
                    for (var date = firstDay; date <= lastDay; date = date.AddDays(1)) {
                        long to = HeartRateMonitor.DailyActivity.Midnight(date.AddDays(1), zone);
                        BindInt(s, 1, from); BindInt(s, 2, to);
                        int rc = Native.sqlite3_step(s); if (rc != Native.Row) Check(rc);
                        result.Add(new DailyActivity { Date = date, Count = Native.sqlite3_column_int64(s, 0) });
                        Check(Native.sqlite3_reset(s));
                        from = to;
                    }
                    return result;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public TimeWindow Bounds()
        {
            lock (gate) {
                // Read both ends of readings_time instead of aggregating every indexed row.
                var s = Prepare("SELECT (SELECT utc_ms FROM readings ORDER BY utc_ms LIMIT 1),(SELECT utc_ms FROM readings ORDER BY utc_ms DESC LIMIT 1);");
                try {
                    int rc = Native.sqlite3_step(s); if (rc != Native.Row) Check(rc);
                    if (Native.sqlite3_column_type(s, 0) == Native.Null) return null;
                    long first = Native.sqlite3_column_int64(s, 0), last = Native.sqlite3_column_int64(s, 1);
                    return new TimeWindow(first, last + 1);
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public List<ChartPoint> Chart(long from, long to, int maximumPoints = 400)
        {
            lock (gate) {
                long bucket = ChartBuckets.Width(from, to, maximumPoints);
                var s = Prepare("SELECT MIN(utc_ms),AVG(bpm),MIN(bpm),MAX(bpm) FROM readings WHERE utc_ms>=? AND utc_ms<? AND " + ValidReadingCondition + " GROUP BY (utc_ms-?)/? ORDER BY MIN(utc_ms);");
                try { BindInt(s, 1, from); BindInt(s, 2, to); BindInt(s, 3, from); BindInt(s, 4, bucket);
                    var points = new List<ChartPoint>(); int rc;
                    while ((rc = Native.sqlite3_step(s)) == Native.Row) points.Add(new ChartPoint { UnixMs = Native.sqlite3_column_int64(s, 0), Bpm = Native.sqlite3_column_double(s, 1), Min = Native.sqlite3_column_double(s, 2), Max = Native.sqlite3_column_double(s, 3) });
                    if (rc != Native.Done) Check(rc); return points;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
    }
}
