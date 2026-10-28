using System.IO;
using System.Linq;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestSleepDeletion(string folder)
    {
        string database = Path.Combine(folder, "sleep-deletion.db");
        var target = new SleepInterval { From = 1000, To = 5000, Excluded = true };
        var retained = new SleepInterval { From = 4000, To = 9000 };
        var ongoing = new SleepInterval { From = 10000, To = 10000, IsOngoing = true };
        using (var store = new Store(database)) {
            store.SaveSleepInterval(target); store.SaveSleepInterval(retained); store.SaveSleepInterval(ongoing);
            store.Add(new Reading { UnixMs = 2000, Bpm = 60, Session = "ISOLATED SLEEP DELETION TEST" });
            store.Add(new Reading { UnixMs = 4500, Bpm = 80, Session = "ISOLATED SLEEP DELETION TEST" });
            store.Add(new Reading { UnixMs = 11000, Bpm = 70, Session = "ISOLATED SLEEP DELETION TEST" });
            var originalReadings = store.Query(0, 20000);
            Check(!store.DeleteSleepInterval(long.MaxValue) && !store.DeleteSleepInterval(0),
                "sleep deletion reports missing IDs without touching existing labels");
            Check(!store.DeleteSleepInterval(ongoing.Id),
                "sleep deletion protects even a zero-duration ongoing observation");
            ongoing.To = 12000; store.SaveSleepInterval(ongoing);
            using (var independent = new Store(database)) {
                Check(!independent.DeleteSleepInterval(ongoing.Id) && independent.SleepIntervals(0, 20000).Single(r => r.Id == ongoing.Id).IsOngoing,
                    "a fresh management connection cannot delete an interval owned by the recorder");
            }

            ExecuteIsolatedSql(database, "CREATE TRIGGER reject_sleep_cleanup BEFORE DELETE ON sleep_management BEGIN SELECT RAISE(ABORT,'ISOLATED SLEEP DELETE FAILURE'); END;");
            Throws<IOException>(() => store.DeleteSleepInterval(target.Id),
                "sleep deletion propagates a SQLite metadata cleanup failure");
            Check(store.SleepIntervals(0, 20000).Single(r => r.Id == target.Id).Excluded,
                "a failed sleep deletion atomically preserves the interval and its management state");
            ExecuteIsolatedSql(database, "DROP TRIGGER reject_sleep_cleanup;");

            Check(store.DeleteSleepInterval(target.Id), "a completed excluded sleep label can be deleted");
            var remaining = store.SleepIntervals(0, 20000);
            Check(remaining.Count == 2 && remaining.Any(r => r.Id == retained.Id) && remaining.Any(r => r.Id == ongoing.Id),
                "sleep deletion removes only the selected label and preserves overlapping labels");
            var readings = store.Query(0, 20000);
            Check(readings.Count == originalReadings.Count && readings.Zip(originalReadings, (after, before) =>
                after.Id == before.Id && after.UnixMs == before.UnixMs && after.Bpm == before.Bpm && after.Session == before.Session).All(same => same),
                "sleep deletion preserves every original heart-rate reading inside and outside the interval");
            Check(!store.DeleteSleepInterval(target.Id), "repeated sleep deletion reports the already removed label");
            target.To = 6000;
            Check(!store.SaveSleepInterval(target) && store.SleepIntervals(0, 20000).All(r => r.Id != target.Id),
                "a stale recorder object cannot resurrect a manually deleted sleep label");
        }
        using (var reopened = new Store(database)) {
            var remaining = reopened.SleepIntervals(0, 20000);
            Check(remaining.Count == 2 && remaining.All(r => r.Id != target.Id) && reopened.Query(0, 20000).Count == 3,
                "sleep label deletion persists across reopening while measurements remain available");
        }
        // Fail the test if deletion (or a stale save) leaves orphaned management metadata.
        ExecuteIsolatedSql(database, "CREATE TABLE sleep_deletion_cleanup_check(value INTEGER NOT NULL CHECK(value=0));");
        ExecuteIsolatedSql(database, "INSERT INTO sleep_deletion_cleanup_check SELECT COUNT(*) FROM sleep_management WHERE sleep_id=" + target.Id + ";");
        Check(true, "sleep deletion cleans up management metadata and stale saves do not recreate it");
    }
}
