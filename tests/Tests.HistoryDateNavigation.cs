using System;
using System.Globalization;
using System.Linq;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestHistoryDateNavigation()
    {
        TimeWindow range; string error;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        Check(HistoryDateNavigation.TryWindow("2024/02/29", "21:30", 600000, zone, out range, out error)
            && range.From == new DateTimeOffset(2024, 2, 29, 21, 30, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds()
            && range.To - range.From == 600000, "date jump preserves leap day, local start time and current span");
        Check(HistoryDateNavigation.TryWindow("2023-12-31", "23:59:30", 600000, zone, out range, out error)
            && DateTimeOffset.FromUnixTimeMilliseconds(range.To).ToOffset(TimeSpan.FromHours(9)).Year == 2024,
            "date jump crosses a year boundary without clamping to recorded dates");
        foreach (var pair in new[] { new[] { "2023/02/29", "12:00" }, new[] { "2024/02/29", "24:00" }, new[] { "", "12:00" }, new[] { "02/03/2024", "12:00" } })
            Check(!HistoryDateNavigation.TryWindow(pair[0], pair[1], 600000, zone, out range, out error) && range == null && error.Length > 0,
                "date jump rejects invalid or ambiguous input " + pair[0] + " " + pair[1]);
        Check(!HistoryDateNavigation.TryWindow("9999/12/31", "23:59", 86400000, zone, out range, out error), "date jump reports overflowing date windows");
        Check(!HistoryDateNavigation.TryWindow("0001/01/01", "00:00", 600000, zone, out range, out error)
            && range == null && error.Length > 0, "date jump rejects local times whose UTC value precedes the supported range instead of silently clamping them");
        Check(HistoryDateNavigation.TryWindow("0001/01/01", "09:00", 600000, zone, out range, out error)
            && range.From == DateTimeOffset.MinValue.ToUnixTimeMilliseconds(), "date jump accepts the exact minimum UTC boundary in a positive-offset zone");
        Check(HistoryDateNavigation.TryWindow("0001/01/01", "00:00", 600000, TimeZoneInfo.Utc, out range, out error)
            && range.From == DateTimeOffset.MinValue.ToUnixTimeMilliseconds(), "date jump preserves the supported minimum date in UTC");
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        Check(!HistoryDateNavigation.TryWindow("2024/03/10", "02:30", 600000, pacific, out range, out error), "date jump rejects nonexistent daylight-saving time");
        Check(HistoryDateNavigation.TryWindow("2024/11/03", "01:30", 600000, pacific, out range, out error)
            && range.From == new DateTimeOffset(2024, 11, 3, 1, 30, 0, TimeSpan.FromHours(-7)).ToUnixTimeMilliseconds(),
            "date jump consistently uses the earlier occurrence of a repeated time");
        var original = CultureInfo.CurrentCulture;
        try {
            var japanese = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone(); japanese.DateTimeFormat.Calendar = new JapaneseCalendar();
            CultureInfo.CurrentCulture = japanese;
            Check(HistoryDateNavigation.TryWindow("2024/02/29", "21:30:15", 600000, zone, out range, out error)
                && DateTimeOffset.FromUnixTimeMilliseconds(range.From).Year == 2024, "date jump uses Gregorian years independently of the OS calendar");
        } finally { CultureInfo.CurrentCulture = original; }
    }
}
