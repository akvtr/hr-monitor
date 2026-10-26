using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HeartRateMonitor
{
    public sealed partial class Store
    {
        private static readonly Encoding utf8Encoding = new UTF8Encoding(false, true);
        private IntPtr db;
        private void OpenDatabase()
        {
            int rc = Native.sqlite3_open_v2(Utf8(databasePath), out db, Native.OpenReadWrite | Native.OpenCreate | Native.OpenFullMutex, IntPtr.Zero);
            if (rc != Native.Ok) throw new IOException(Text(Native.sqlite3_errmsg(db)));
            Check(Native.sqlite3_busy_timeout(db, 5000));
        }
        private static byte[] Utf8(string value) => utf8Encoding.GetBytes(value + "\0");
        private static string Text(IntPtr p) {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
            return Text(p, n);
        }
        private static string Text(IntPtr p, int length)
        {
            if (p == IntPtr.Zero || length == 0) return "";
            var bytes = new byte[length];
            Marshal.Copy(p, bytes, 0, length);
            return utf8Encoding.GetString(bytes);
        }
        private static string ColumnText(IntPtr statement, int column)
        {
            // SQLite text values have an explicit byte length and can contain embedded NULs.
            IntPtr text = Native.sqlite3_column_text(statement, column);
            return Text(text, Native.sqlite3_column_bytes(statement, column));
        }

        private void Check(int rc) { if (rc != Native.Ok) throw new IOException("SQLite: " + Text(Native.sqlite3_errmsg(db))); }
        private IntPtr Prepare(string sql)
        {
            if (db == IntPtr.Zero) throw new ObjectDisposedException(nameof(Store));
            IntPtr stmt;
            Check(Native.sqlite3_prepare_v2(db, Utf8(sql), -1, out stmt, IntPtr.Zero));
            return stmt;
        }
        private void Execute(string sql)
        {
            lock (gate) {
                var statement = Prepare(sql);
                try {
                    int result;
                    do { result = Native.sqlite3_step(statement); } while (result == Native.Row);
                    if (result != Native.Done) Check(result);
                } finally { Native.sqlite3_finalize(statement); }
            }
        }
        private void RollbackActiveTransaction()
        {
            // A closed connection or an automatic SQLite rollback already ended the transaction.
            if (db != IntPtr.Zero && Native.sqlite3_get_autocommit(db) == 0) Execute("ROLLBACK;");
        }
        private void BindText(IntPtr s, int n, string text)
        {
            var bytes = Utf8(text ?? "");
            Check(Native.sqlite3_bind_text(s, n, bytes, bytes.Length - 1, Native.Transient));
        }
        private void BindInt(IntPtr s, int n, long number) => Check(Native.sqlite3_bind_int64(s, n, number));

        public void Dispose()
        {
            lock (gate) {
                if (db == IntPtr.Zero) return;
                Native.sqlite3_close_v2(db);
                db = IntPtr.Zero;
            }
        }
        private static class Native
        {
            private const string Dll = "winsqlite3.dll";
            internal const int Ok = 0;
            internal const int Row = 100;
            internal const int Done = 101;
            internal const int Null = 5;
            internal const int OpenReadWrite = 0x00000002;
            internal const int OpenCreate = 0x00000004;
            internal const int OpenFullMutex = 0x00010000;
            internal static readonly IntPtr Transient = new IntPtr(-1);

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
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_bytes(IntPtr s, int n);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_type(IntPtr s, int n);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_last_insert_rowid(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_changes(IntPtr db);
            [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_get_autocommit(IntPtr db);
        }
    }
}
