using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HeartRateMonitor
{
    // Windows 10+ ships the SQLite C API in winsqlite3.dll. All parameters are bound.
    public sealed class Store : IDisposable
    {
        private IntPtr db;
        private readonly object gate = new object();
        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");
        private static string Text(IntPtr p) {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
            var bytes = new byte[n]; Marshal.Copy(p, bytes, 0, n); return Encoding.UTF8.GetString(bytes);
        }
        public Store(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            int rc = Native.sqlite3_open_v2(Utf8(path), out db, 2 | 4 | 0x10000, IntPtr.Zero);
            if (rc != 0) { string message = Text(Native.sqlite3_errmsg(db)); Dispose(); throw new IOException(message); }
            try {
                Check(Native.sqlite3_busy_timeout(db, 5000));
                Execute("PRAGMA journal_mode=WAL;");
                Execute("PRAGMA synchronous=FULL;");
                Execute("CREATE TABLE IF NOT EXISTS readings (id INTEGER PRIMARY KEY AUTOINCREMENT, utc_ms INTEGER NOT NULL, session TEXT NOT NULL, device TEXT NOT NULL, bpm INTEGER NOT NULL, contact INTEGER, rr_ms TEXT NOT NULL, energy INTEGER);");
                Execute("CREATE INDEX IF NOT EXISTS readings_time ON readings(utc_ms);");
            } catch { Dispose(); throw; }
        }
        private void Check(int rc) { if (rc != 0) throw new IOException("SQLite: " + Text(Native.sqlite3_errmsg(db))); }
        private IntPtr Prepare(string sql) { IntPtr stmt; Check(Native.sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero)); return stmt; }
        private void Execute(string sql) { lock (gate) { var stmt = Prepare(sql); try { int rc; do { rc = Native.sqlite3_step(stmt); } while (rc == 100); if (rc != 101) Check(rc); } finally { Native.sqlite3_finalize(stmt); } } }
        private void BindText(IntPtr s, int n, string text) { var bytes = Utf8(text ?? ""); Check(Native.sqlite3_bind_text(s, n, bytes, bytes.Length - 1, new IntPtr(-1))); }
        private void BindInt(IntPtr s, int n, long number) => Check(Native.sqlite3_bind_int64(s, n, number));
        public void Add(Reading row)
        {
            lock (gate) {
                var s = Prepare("INSERT INTO readings(utc_ms,session,device,bpm,contact,rr_ms,energy) VALUES(?,?,?,?,?,?,?);");
                try {
                    BindInt(s, 1, row.UnixMs); BindText(s, 2, row.Session); BindText(s, 3, row.Device); BindInt(s, 4, row.Bpm);
                    if (row.Contact.HasValue) BindInt(s, 5, row.Contact.Value ? 1 : 0);
                    BindText(s, 6, row.Rr); if (row.Energy.HasValue) BindInt(s, 7, row.Energy.Value);
                    int rc = Native.sqlite3_step(s); if (rc != 101) Check(rc);
                    row.Id = Native.sqlite3_last_insert_rowid(db);
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public int DeleteBefore(long utcMs)
        {
            lock (gate) {
                var s = Prepare("DELETE FROM readings WHERE utc_ms < ?;");
                try {
                    BindInt(s, 1, utcMs);
                    int rc = Native.sqlite3_step(s); if (rc != 101) Check(rc);
                    return Native.sqlite3_changes(db);
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        private Reading Row(IntPtr s) => new Reading {
            Id = Native.sqlite3_column_int64(s, 0), UnixMs = Native.sqlite3_column_int64(s, 1),
            Session = Text(Native.sqlite3_column_text(s, 2)), Device = Text(Native.sqlite3_column_text(s, 3)),
            Bpm = (int)Native.sqlite3_column_int64(s, 4),
            Contact = Native.sqlite3_column_type(s, 5) == 5 ? (bool?)null : Native.sqlite3_column_int64(s, 5) != 0,
            Rr = Text(Native.sqlite3_column_text(s, 6)), Energy = Native.sqlite3_column_type(s, 7) == 5 ? (int?)null : (int)Native.sqlite3_column_int64(s, 7)
        };
        public List<Reading> Query(long from, long to, int limit = 1000)
        {
            lock (gate) {
                var result = new List<Reading>();
                var s = Prepare("SELECT id,utc_ms,session,device,bpm,contact,rr_ms,energy FROM readings WHERE utc_ms>=? AND utc_ms<? ORDER BY utc_ms DESC,id DESC LIMIT ?;");
                try { BindInt(s, 1, from); BindInt(s, 2, to); BindInt(s, 3, limit); int rc;
                    while ((rc = Native.sqlite3_step(s)) == 100) result.Add(Row(s));
                    if (rc != 101) Check(rc); return result;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public HistorySummary Summary(long from, long to)
        {
            lock (gate) {
                var s = Prepare("SELECT COUNT(*),MIN(bpm),AVG(bpm),MAX(bpm) FROM readings WHERE utc_ms>=? AND utc_ms<? AND bpm>0 AND (contact IS NULL OR contact=1);");
                try { BindInt(s, 1, from); BindInt(s, 2, to); int rc = Native.sqlite3_step(s); if (rc != 100) Check(rc);
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
                var s = Prepare("SELECT COUNT(*) FROM readings WHERE utc_ms>=? AND utc_ms<? AND bpm>0 AND (contact IS NULL OR contact=1);");
                try {
                    for (var date = firstDay; date <= lastDay; date = date.AddDays(1)) {
                        BindInt(s, 1, HeartRateMonitor.DailyActivity.Midnight(date, zone));
                        BindInt(s, 2, HeartRateMonitor.DailyActivity.Midnight(date.AddDays(1), zone));
                        int rc = Native.sqlite3_step(s); if (rc != 100) Check(rc);
                        result.Add(new DailyActivity { Date = date, Count = Native.sqlite3_column_int64(s, 0) });
                        Check(Native.sqlite3_reset(s));
                    }
                    return result;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public TimeWindow Bounds()
        {
            lock (gate) {
                var s = Prepare("SELECT MIN(utc_ms),MAX(utc_ms) FROM readings;");
                try {
                    int rc = Native.sqlite3_step(s); if (rc != 100) Check(rc);
                    if (Native.sqlite3_column_type(s, 0) == 5) return null;
                    long first = Native.sqlite3_column_int64(s, 0), last = Native.sqlite3_column_int64(s, 1);
                    return new TimeWindow(first, last + 1);
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public List<ChartPoint> Chart(long from, long to, int maximumPoints = 400)
        {
            lock (gate) {
                if (maximumPoints < 1 || maximumPoints > 14400) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
                long bucket = Math.Max(1000, (to - from + maximumPoints - 1) / maximumPoints);
                var s = Prepare("SELECT MIN(utc_ms),AVG(bpm),MIN(bpm),MAX(bpm) FROM readings WHERE utc_ms>=? AND utc_ms<? AND bpm>0 AND (contact IS NULL OR contact=1) GROUP BY (utc_ms-?)/? ORDER BY MIN(utc_ms);");
                try { BindInt(s, 1, from); BindInt(s, 2, to); BindInt(s, 3, from); BindInt(s, 4, bucket);
                    var points = new List<ChartPoint>(); int rc;
                    while ((rc = Native.sqlite3_step(s)) == 100) points.Add(new ChartPoint { UnixMs = Native.sqlite3_column_int64(s, 0), Bpm = Native.sqlite3_column_double(s, 1), Min = Native.sqlite3_column_double(s, 2), Max = Native.sqlite3_column_double(s, 3) });
                    if (rc != 101) Check(rc); return points;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        public long Export(string path, long from, long to)
        {
            lock (gate) {
                var s = Prepare("SELECT id,utc_ms,session,device,bpm,contact,rr_ms,energy FROM readings WHERE utc_ms>=? AND utc_ms<? ORDER BY utc_ms,id;");
                try {
                    BindInt(s, 1, from); BindInt(s, 2, to); long count = 0; int rc;
                    using (var writer = new StreamWriter(path, false, new UTF8Encoding(true))) {
                        writer.WriteLine("utc_time,local_time,bpm,contact,rr_ms,energy_kj,device,session");
                        while ((rc = Native.sqlite3_step(s)) == 100) { var r = Row(s);
                            writer.WriteLine(string.Join(",", Csv(DateTimeOffset.FromUnixTimeMilliseconds(r.UnixMs).ToString("O")), Csv(r.Time), r.Bpm.ToString(CultureInfo.InvariantCulture), r.Contact.HasValue ? (r.Contact.Value ? "1" : "0") : "", Csv(r.Rr), r.Energy?.ToString(CultureInfo.InvariantCulture) ?? "", Csv(r.Device), Csv(r.Session))); count++;
                        }
                    }
                    if (rc != 101) Check(rc); return count;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
        public void Dispose() { lock (gate) { if (db != IntPtr.Zero) { Native.sqlite3_close_v2(db); db = IntPtr.Zero; } } }
        private static class Native
        {
            private const string Dll = "winsqlite3.dll";
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_busy_timeout(IntPtr db, int ms);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr s, IntPtr tail);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr s);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr s);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_reset(IntPtr s);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_int64(IntPtr s, int n, long value);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_bind_text(IntPtr s, int n, byte[] value, int length, IntPtr destructor);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_column_int64(IntPtr s, int n);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern double sqlite3_column_double(IntPtr s, int n);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr s, int n);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_type(IntPtr s, int n);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_last_insert_rowid(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_changes(IntPtr db);
        }
    }
    public sealed class HistorySummary { public long Count; public int Min; public double Average; public int Max; }
    public sealed class ChartPoint { public long UnixMs; public double Bpm; public double Min; public double Max; }
}
