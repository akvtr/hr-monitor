using System;
using System.Linq;

namespace HeartRateMonitor
{
    public sealed class DailyActivity
    {
        public DateTime Date;
        public long Count;
        // Convert each midnight separately so daylight-saving days need not be 24 hours.
        public static long Midnight(DateTime date, TimeZoneInfo zone)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            var local = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            long lastInvalid = local.Ticks;
            // Bracket the first valid time, then refine the final minute without rounding away seconds.
            while (zone.IsInvalidTime(local)) {
                lastInvalid = local.Ticks;
                local = local.AddMinutes(1);
            }
            long firstValid = local.Ticks;
            while (firstValid - lastInvalid > 1) {
                long middle = lastInvalid + (firstValid - lastInvalid) / 2;
                if (zone.IsInvalidTime(new DateTime(middle, DateTimeKind.Unspecified))) lastInvalid = middle;
                else firstValid = middle;
            }
            local = new DateTime(firstValid, DateTimeKind.Unspecified);
            if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUnixTimeMilliseconds();
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)).ToUnixTimeMilliseconds();
        }
        public static int Level(long count) => count <= 0 ? 0 : count < 600 ? 1 : count < 3600 ? 2 : count < 10800 ? 3 : 4;
    }
}
