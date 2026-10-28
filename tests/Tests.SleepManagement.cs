using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static long SleepUtc(int day, int hour, int minute = 0)
        => new DateTimeOffset(2026, 1, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static void TestSleepManagement(string folder)
    {
        Check(typeof(Store).GetMethod("DeleteSleepInterval") == null, "sleep records have no manual deletion API");
        Check(new Settings().SleepDayBoundaryHour == 0, "sleep day defaults to local midnight");
        var preferences = new Settings { SleepDayBoundaryHour = 4 };
        string preferencePath = Path.Combine(folder, "sleep-management-settings.json"); preferences.Save(preferencePath);
        Check(Settings.Load(preferencePath).SleepDayBoundaryHour == 4 && preferences.Copy().SleepDayBoundaryHour == 4,
            "sleep day boundary persists and survives settings copying");
        File.WriteAllText(preferencePath, "{}");
        Check(Settings.Load(preferencePath).SleepDayBoundaryHour == 0, "settings without a saved sleep boundary use midnight");
        foreach (int hour in new[] { 0, 12, 23 }) {
            preferences.SleepDayBoundaryHour = hour; preferences.Save(preferencePath);
            Check(Settings.Load(preferencePath).SleepDayBoundaryHour == hour,
                "explicit sleep boundary survives reload, including the former default: " + hour);
        }
        Throws<ArgumentException>(() => new Settings { SleepDayBoundaryHour = -1 }.Validate(), "sleep boundary rejects negative hours");
        Throws<ArgumentException>(() => new Settings { SleepDayBoundaryHour = 24 }.Validate(), "sleep boundary rejects hours after 23");
        string database = Path.Combine(folder, "sleep-management.db");
        long excludedId, ongoingId;
        using (var store = new Store(database)) {
            var empty = store.LoadSleepAnalysis(new DateTime(2026, 1, 1), new DateTime(2026, 1, 5), TimeZoneInfo.Utc);
            Check(empty.Records.Count == 0 && empty.RecordedDays == 0 && !empty.AverageHeartRate.HasValue && !empty.AverageDurationMilliseconds.HasValue,
                "empty sleep analysis has no invented zero averages");
            store.SaveSleepInterval(new SleepInterval { From = SleepUtc(1, 23), To = SleepUtc(2, 7) });
            store.SaveSleepInterval(new SleepInterval { From = SleepUtc(2, 0), To = SleepUtc(2, 2) });
            store.SaveSleepInterval(new SleepInterval { From = SleepUtc(2, 23), To = SleepUtc(3, 2) });
            store.SaveSleepInterval(new SleepInterval { From = SleepUtc(3, 3), To = SleepUtc(3, 7) });
            var excluded = new SleepInterval { From = SleepUtc(3, 23), To = SleepUtc(4, 7), Excluded = true };
            var ongoing = new SleepInterval { From = SleepUtc(4, 23), To = SleepUtc(5, 7), IsOngoing = true };
            store.SaveSleepInterval(excluded); store.SaveSleepInterval(ongoing); excludedId = excluded.Id; ongoingId = ongoing.Id;
            foreach (var sample in new[] {
                new Reading { UnixMs = SleepUtc(1, 23), Bpm = 60 },
                new Reading { UnixMs = SleepUtc(2, 6), Bpm = 80 },
                new Reading { UnixMs = SleepUtc(3, 1), Bpm = 100 },
                new Reading { UnixMs = SleepUtc(3, 2, 30), Bpm = 200 },
                new Reading { UnixMs = SleepUtc(3, 3), Bpm = 0 },
                new Reading { UnixMs = SleepUtc(3, 4), Bpm = 180, Contact = false },
                new Reading { UnixMs = SleepUtc(3, 7), Bpm = 190 },
                new Reading { UnixMs = SleepUtc(4, 0), Bpm = 210 },
                new Reading { UnixMs = SleepUtc(5, 0), Bpm = 220 }
            }) store.Add(sample);
            var midnightAnalysis = store.LoadSleepAnalysis(new DateTime(2026, 1, 1), new DateTime(2026, 1, 5), TimeZoneInfo.Utc);
            Check(midnightAnalysis.RecordedDays == 3 && midnightAnalysis.Days[0].DurationMilliseconds == 3600000 &&
                midnightAnalysis.Days[1].DurationMilliseconds == 8 * 3600000 && midnightAnalysis.Days[2].DurationMilliseconds == 6 * 3600000,
                "default midnight analysis splits cross-date sleep at calendar midnight without changing observed duration");
            var analysis = store.LoadSleepAnalysis(new DateTime(2026, 1, 1), new DateTime(2026, 1, 5), TimeZoneInfo.Utc, 12);
            Check(analysis.Records.Count == 6 && analysis.IncludedCount == 4 && analysis.Days.Count == 5,
                "sleep records retain excluded and ongoing rows while aggregates select only finished included intervals");
            Check(analysis.RecordedDays == 2 && analysis.AverageDurationMilliseconds == 7.5 * 3600000 && analysis.Days[1].DurationMilliseconds == 7 * 3600000,
                "sleep averages use observed days and preserve gaps instead of inserting missing nights or communication time");
            Check(analysis.Days[0].DurationMilliseconds == 8 * 3600000 && analysis.ValidReadingCount == 3 && analysis.AverageHeartRate == 80 && analysis.HeartRateDays == 2,
                "overlapping sleep labels do not double-count time or valid heart readings and sleep gaps and invalid samples are excluded");
            Check(analysis.ShortestDurationMilliseconds == 7 * 3600000 && analysis.LongestDurationMilliseconds == 8 * 3600000,
                "sleep duration extremes compare daily observed totals");
            Check(analysis.TypicalStartMinutes == 13 * 60 && analysis.TypicalEndMinutes == 19 * 60,
                "typical sleep times use longest continuous range medians relative to noon without midnight averaging errors");
            Check(analysis.Records.First().Summary.Count == 2 && analysis.Records.First().Summary.Average == 70,
                "individual sleep records expose valid heart-rate statistics");
            Check(!store.SetSleepExcluded(ongoing.Id, true),
                "ongoing sleep cannot be excluded underneath the recorder");
            Check(!store.SetSleepExcluded(long.MaxValue, true),
                "sleep management reports missing records without creating metadata");
            Check(store.SetSleepExcluded(excluded.Id, false), "sleep exclusion can be restored without deleting a record");
            excluded.Excluded = true; excluded.To += 1000; store.SaveSleepInterval(excluded);
            Check(!store.SleepIntervals(SleepUtc(3, 22), SleepUtc(4, 8)).Single().Excluded,
                "recorder updates preserve the separately saved exclusion preference");
            store.SetSleepExcluded(excluded.Id, true);
            var clipped = store.LoadSleepAnalysis(new DateTime(2026, 1, 2), new DateTime(2026, 1, 2), TimeZoneInfo.Utc, 0);
            Check(clipped.RecordedDays == 1 && clipped.AverageDurationMilliseconds == 8 * 3600000,
                "sleep analysis clips crossing intervals to the selected day boundaries");
            Throws<ArgumentOutOfRangeException>(() => store.LoadSleepAnalysis(new DateTime(2026, 1, 2), new DateTime(2026, 1, 1), TimeZoneInfo.Utc),
                "sleep analysis rejects reversed date ranges");
            Throws<ArgumentOutOfRangeException>(() => store.LoadSleepAnalysis(new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), TimeZoneInfo.Utc, 24),
                "sleep day boundary hours are validated");
        }
        using (var reopened = new Store(database)) {
            var rows = reopened.SleepIntervals(SleepUtc(1, 0), SleepUtc(6, 0));
            Check(rows.Single(r => r.Id == excludedId).Excluded && rows.Single(r => r.Id == ongoingId).IsOngoing,
                "exclusion and ongoing metadata survive database reopening without query side effects");
            reopened.CloseStaleSleepIntervals();
            Check(!reopened.SleepIntervals(SleepUtc(4, 22), SleepUtc(5, 8)).Single().IsOngoing,
                "startup closes stale tracking markers without removing the finished record");
            Check(reopened.Query(SleepUtc(1, 0), SleepUtc(6, 0)).Count == 9,
                "closing stale sleep markers preserves all heart-rate readings");
            reopened.DeleteBefore(SleepUtc(5, 8));
            Check(reopened.SleepIntervals(SleepUtc(1, 0), SleepUtc(6, 0)).Count == 0,
                "retention still deletes completed sleep records with management metadata");
        }

        string trackerDatabase = Path.Combine(folder, "sleep-management-tracker.db");
        using (var store = new Store(trackerDatabase)) {
            var tracker = new SleepIntervalTracker(store);
            tracker.Observe(true, 1000); tracker.Observe(true, 2000);
            Check(store.SleepIntervals(0, 10000).Single().IsOngoing, "sleep tracker persists an ongoing marker while recording");
            tracker.Observe(null, 3000);
            var finished = store.SleepIntervals(0, 10000).Single();
            Check(!finished.IsOngoing && finished.To == 2000, "unknown sleep state closes recording at its last confirmed observation");
            store.DeleteBefore(3000);
            Check(store.SleepIntervals(0, 10000).Count == 0, "finite retention still removes expired finished sleep intervals");
            finished.To = 4000; store.SaveSleepInterval(finished);
            Check(store.SleepIntervals(0, 10000).Count == 0, "a stale recorder object cannot resurrect a retention-pruned sleep interval");
            tracker.Observe(true, 5000); tracker.Observe(true, 6000); tracker.Close();
            Check(!store.SleepIntervals(0, 10000).Single().IsOngoing && store.SleepIntervals(0, 10000).Single().To == 6000,
                "stopping tracking closes the marker without extending confirmed sleep");
            tracker.Observe(true, 7000); tracker.Observe(true, 8000);
            store.DeleteBefore(9000);
            tracker.Observe(true, 9000); tracker.Observe(true, 10000);
            var resumed = store.SleepIntervals(0, 20000).Single();
            Check(resumed.From == 9000 && resumed.To == 10000 && resumed.IsOngoing,
                "retention-pruned ongoing labels resume at a fresh confirmed observation without restoring expired sleep");
        }

        string migrationDatabase = Path.Combine(folder, "sleep-management-migration.db");
        using (var store = new Store(migrationDatabase)) store.SaveSleepInterval(new SleepInterval { From = 1000, To = 2000 });
        ExecuteIsolatedSql(migrationDatabase, "DROP TRIGGER sleep_management_cleanup;");
        ExecuteIsolatedSql(migrationDatabase, "DROP TABLE sleep_management;");
        using (var migrated = new Store(migrationDatabase)) {
            var original = migrated.SleepIntervals(0, 10000).Single();
            Check(!original.Excluded && !original.IsOngoing && original.From == 1000 && original.To == 2000,
                "legacy sleep databases migrate without changing original intervals");
        }
        string failureDatabase = Path.Combine(folder, "sleep-management-marker-failure.db");
        ExecuteIsolatedSql(failureDatabase, "CREATE TRIGGER reject_sleep_marker BEFORE INSERT ON sleep_management BEGIN SELECT RAISE(ABORT,'ISOLATED MARKER FAILURE'); END;");
        using (var store = new Store(failureDatabase)) {
            Throws<IOException>(() => store.SaveSleepInterval(new SleepInterval { From = 1000, To = 2000, IsOngoing = true }),
                "sleep marker persistence failures are reported");
            Check(store.SleepIntervals(0, 10000).Count == 0, "sleep record and ongoing marker are saved atomically when metadata persistence fails");
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        long springFrom = Store.SleepDayBoundary(new DateTime(2026, 3, 7), zone, 12);
        long springTo = Store.SleepDayBoundary(new DateTime(2026, 3, 8), zone, 12);
        Check(springTo - springFrom == 23 * 3600000, "sleep day boundaries preserve elapsed duration through daylight saving changes");
        var midnightBoundaries = typeof(HeartChart).GetMethod("MidnightBoundaries", BindingFlags.NonPublic | BindingFlags.Static);
        long midnightStart = Store.SleepDayBoundary(new DateTime(2026, 3, 8), zone, 0);
        long midnightEnd = Store.SleepDayBoundary(new DateTime(2026, 3, 9), zone, 0);
        var dates = (System.Collections.Generic.List<long>)midnightBoundaries.Invoke(null, new object[] { midnightStart - 3600000, midnightEnd + 3600000, zone });
        Check(dates.Count == 2 && dates[0] == midnightStart && dates[1] == midnightEnd && dates[1] - dates[0] == 23 * 3600000,
            "heart chart midnight separators follow local calendar dates through daylight saving changes");
        dates = (System.Collections.Generic.List<long>)midnightBoundaries.Invoke(null, new object[] { midnightStart, midnightEnd, zone });
        Check(dates.Count == 0, "midnight separators exclude chart endpoints instead of duplicating edge lines");
        RunOnSta(() => {
            var chart = new SleepRhythmChart { Zone = TimeZoneInfo.Utc };
            var position = typeof(SleepRhythmChart).GetMethod("MidnightPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (int hour in new[] { 0, 4, 6, 12, 23 }) {
                chart.BoundaryHour = hour;
                double x = (double)position.Invoke(chart, new object[] { new DateTime(2026, 1, 1) });
                Check(Math.Abs(x - (24 - hour) / 24.0) < 0.000001,
                    "sleep timeline locates midnight independently of coarse hour ticks: " + hour);
            }
            chart.Zone = zone; chart.BoundaryHour = 12;
            Check(Math.Abs((double)position.Invoke(chart, new object[] { new DateTime(2026, 3, 7) }) - 12 / 23.0) < 0.000001,
                "sleep timeline midnight marker respects actual elapsed duration on a daylight-saving day");
        }, "Sleep midnight marker geometry verification failed");

        string monitorDatabase = Path.Combine(folder, "sleep-management-monitor.db");
        using (var initialized = new Store(monitorDatabase)) { }
        int unavailable = 0;
        var monitorSettings = new Settings { OyasumiEnabled = true, OyasumiQueryPort = 1 };
        var monitor = new OyasumiSleepMonitor(monitorDatabase, monitorSettings,
            read: (port, token) => { if (Volatile.Read(ref unavailable) != 0) throw new IOException("isolated unavailable"); return true; },
            interval: TimeSpan.FromMilliseconds(30));
        try {
            monitor.Start();
            WaitUntil(() => { using (var store = new Store(monitorDatabase)) return store.SleepIntervals(0, long.MaxValue).Count == 1; });
            monitorSettings.OyasumiEnabled = false; monitor.Configure(monitorSettings);
            WaitUntil(() => monitor.Status.State == "Disabled");
            using (var store = new Store(monitorDatabase))
                Check(!store.SleepIntervals(0, long.MaxValue).Single().IsOngoing, "disabling integration finishes its ongoing marker");
            monitorSettings.OyasumiEnabled = true; monitor.Configure(monitorSettings);
            WaitUntil(() => { using (var store = new Store(monitorDatabase)) return store.SleepIntervals(0, long.MaxValue).Count == 2; });
            Interlocked.Exchange(ref unavailable, 1);
            WaitUntil(() => monitor.Status.State == "Unavailable");
            using (var store = new Store(monitorDatabase))
                Check(store.SleepIntervals(0, long.MaxValue).All(i => !i.IsOngoing), "communication failure closes ongoing markers at the last observation");
            Interlocked.Exchange(ref unavailable, 0);
            WaitUntil(() => { using (var store = new Store(monitorDatabase)) return store.SleepIntervals(0, long.MaxValue).Count == 3; });
        } finally { Check(monitor.StopAsync().Wait(3000), "sleep management monitor stops within the expected timeout"); }
        using (var store = new Store(monitorDatabase))
            Check(store.SleepIntervals(0, long.MaxValue).All(i => !i.IsOngoing), "monitor shutdown finishes all active markers");

        RunOnSta(() => {
            string directory = Path.Combine(folder, "sleep-management-startup-locked");
            string lockedDatabase = Path.Combine(directory, "heartrate.db");
            new Settings { Language = "en", AutoConnect = false, CloseToTray = false, OscEnabled = false, OyasumiEnabled = false }
                .Save(Path.Combine(directory, "settings.json"));
            using (var held = new Store(lockedDatabase)) {
                held.SaveSleepInterval(new SleepInterval { From = 1000, To = 2000, IsOngoing = true });
                var execute = typeof(Store).GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic);
                execute.Invoke(held, new object[] { "BEGIN IMMEDIATE;" });
                MainWindow window = null;
                try {
                    window = new MainWindow(directory);
                    Check(File.ReadAllText(Path.Combine(directory, "connection.log")).Contains("Could not prepare sleep recording:"),
                        "locked sleep startup cleanup is reported without preventing the application window from opening");
                    Check(held.SleepIntervals(0, 10000).Single().IsOngoing,
                        "failed startup cleanup leaves the locked original sleep record unchanged");
                } finally {
                    execute.Invoke(held, new object[] { "ROLLBACK;" });
                    if (window != null) { window.EndSession(); window.Close(); }
                }
            }
        }, "Sleep startup write-lock recovery verification failed");
    }
}
