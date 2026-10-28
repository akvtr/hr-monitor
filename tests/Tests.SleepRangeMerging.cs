using System;
using System.IO;
using System.Linq;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestSleepRangeMerging(string folder)
    {
        using (var store = new Store(Path.Combine(folder, "sleep-range-merging.db"))) {
            var later = new SleepInterval { From = SleepUtc(1, 5), To = SleepUtc(1, 7) };
            var first = new SleepInterval { From = SleepUtc(1, 1), To = SleepUtc(1, 2) };
            var adjacent = new SleepInterval { From = SleepUtc(1, 2), To = SleepUtc(1, 3) };
            var nested = new SleepInterval { From = SleepUtc(1, 1, 30), To = SleepUtc(1, 2, 30) };
            foreach (var interval in new[] { later, first, adjacent, nested }) store.SaveSleepInterval(interval);
            store.Add(new Reading { UnixMs = SleepUtc(1, 2), Bpm = 60 });
            store.Add(new Reading { UnixMs = SleepUtc(1, 3), Bpm = 200 });
            store.Add(new Reading { UnixMs = SleepUtc(1, 5), Bpm = 80 });
            store.Add(new Reading { UnixMs = SleepUtc(1, 7), Bpm = 200 });

            var analysis = store.LoadSleepAnalysis(new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), TimeZoneInfo.Utc);
            Check(analysis.Days.Single().DurationMilliseconds == 4 * 3600000 && analysis.ValidReadingCount == 2 && analysis.AverageHeartRate == 70,
                "sleep range union counts touching and nested intervals once and excludes each range end");
            Check(analysis.TypicalStartMinutes == 60 && analysis.TypicalEndMinutes == 180,
                "touching sleep ranges merge and equal-length principal ranges select the earlier start");
            var fragments = analysis.Days.Single().Fragments;
            Check(fragments.Select(f => f.IntervalId).SequenceEqual(new[] { first.Id, nested.Id, adjacent.Id, later.Id }) &&
                fragments[0].To == first.To && fragments[1].From == nested.From && fragments[1].To == nested.To,
                "sleep range union preserves individual label identities, order, and overlapping boundaries");
        }
    }
}
