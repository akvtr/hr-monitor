using System;
using System.Globalization;

namespace HeartRateMonitor
{
    public sealed class Reading
    {
        // Strings and value types make a shallow copy sufficient for queue ownership.
        internal Reading Copy() => (Reading)MemberwiseClone();
        public long Id { get; set; }
        public long UnixMs { get; set; }
        public string Session { get; set; }
        public string Device { get; set; }
        public int Bpm { get; set; }
        public bool? Contact { get; set; }
        public string Rr { get; set; }
        public int? Energy { get; set; }
        public string Time => DateTimeOffset.FromUnixTimeMilliseconds(UnixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }
}
