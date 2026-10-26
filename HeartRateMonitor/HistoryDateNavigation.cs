using System;
using System.Globalization;
using System.Linq;

namespace HeartRateMonitor
{
    internal static class HistoryDateNavigation
    {
        internal static bool TryWindow(string date, string time, long width, TimeZoneInfo zone, out TimeWindow range, out string error)
        {
            range = null; error = "";
            DateTime day, clock;
            if (!DateTime.TryParseExact(date.Trim(), new[] { "yyyy/MM/dd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
                || !DateTime.TryParseExact(time.Trim(), new[] { "HH:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out clock)) {
                error = Words.T("日付は yyyy/MM/dd、時刻は HH:mm または HH:mm:ss で入力してください。", "Enter a date as yyyy/MM/dd and a time as HH:mm or HH:mm:ss."); return false;
            }
            var local = DateTime.SpecifyKind(day.Date.Add(clock.TimeOfDay), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) {
                error = Words.T("夏時間の切り替えで存在しない時刻です。別の時刻を指定してください。", "This time does not exist due to daylight saving. Choose another time."); return false;
            }
            // Repeated local times consistently select the earlier occurrence.
            try {
                long from = zone.IsAmbiguousTime(local) ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUnixTimeMilliseconds()
                    // ConvertTimeToUtc silently clamps at DateTime's limits; explicit offsets reject
                    // unrepresentable UTC values rather than navigating to a different local time.
                    : new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUnixTimeMilliseconds();
                long to = checked(from + Math.Max(60000, width));
                DateTimeOffset.FromUnixTimeMilliseconds(to).ToLocalTime();
                if (local.Year == 9999 && local.Month == 12) throw new ArgumentOutOfRangeException();
                range = new TimeWindow(from, to); return true;
            } catch (ArgumentOutOfRangeException) {
                error = Words.T("表示できる日時の範囲を超えています。日付または表示幅を変更してください。", "This exceeds the supported date range. Change the date or time span."); return false;
            } catch (OverflowException) {
                error = Words.T("表示幅が長すぎます。短い表示幅を指定してください。", "The time span is too long. Choose a shorter span."); return false;
            }
        }
    }
}
