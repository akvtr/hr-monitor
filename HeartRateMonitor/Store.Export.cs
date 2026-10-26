using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HeartRateMonitor
{
    public sealed partial class Store
    {
        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        public long Export(string path, long from, long to)
        {
            string destination = Path.GetFullPath(path);
            foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
                if (string.Equals(destination, databasePath + suffix, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(Words.T("データベースと同じ場所にはCSVを書き出せません。別のファイルを選択してください。", "Cannot export CSV over database files. Choose another file."));
            long count = 0;
            AtomicFile.Write(destination, stream => count = WriteCsv(stream, from, to));
            return count;
        }
        private long WriteCsv(Stream stream, long from, long to)
        {
            lock (gate) {
                var s = Prepare("SELECT id,utc_ms,session,device,bpm,contact,rr_ms,energy FROM readings WHERE utc_ms>=? AND utc_ms<? ORDER BY utc_ms,id;");
                try {
                    BindInt(s, 1, from); BindInt(s, 2, to); long count = 0; int rc;
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 1024, true)) {
                        writer.WriteLine("utc_time,local_time,bpm,contact,rr_ms,energy_kj,device,session");
                        while ((rc = Native.sqlite3_step(s)) == Native.Row) {
                            var reading = Row(s);
                            writer.WriteLine(string.Join(",",
                                Csv(DateTimeOffset.FromUnixTimeMilliseconds(reading.UnixMs).ToString("O", CultureInfo.InvariantCulture)),
                                Csv(reading.Time),
                                reading.Bpm.ToString(CultureInfo.InvariantCulture),
                                reading.Contact.HasValue ? (reading.Contact.Value ? "1" : "0") : "",
                                Csv(reading.Rr),
                                reading.Energy?.ToString(CultureInfo.InvariantCulture) ?? "",
                                Csv(reading.Device),
                                Csv(reading.Session)));
                            count++;
                        }
                    }
                    if (rc != Native.Done) Check(rc); return count;
                } finally { Native.sqlite3_finalize(s); }
            }
        }
    }
}
