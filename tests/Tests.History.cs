using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestHistorySelection()
    {
        foreach (long width in new long[] { 60000, 600000, 1800000, 3600000, 86400000, 365L * 86400000 })
            Check(TimeSpanPicker.Decode(TimeSpanPicker.Encode(width), 365L * 86400000) == width, "time-span slider round-trips " + width);
        Check(TimeSpanPicker.Decode(-5, 3600000) == 60000, "time-span slider respects one-minute minimum");
        Check(TimeSpanPicker.Decode(30, 7123456) == 7123456, "time-span slider reaches exact recording boundary");
        Check(TimeSpanPicker.Decode(TimeSpanPicker.Encode(7140001), 7140001) == 7140001, "slider endpoint includes final sample without rounding down");
        var domain = new TimeWindow(0, 86400000);
        var range = new TimeWindow(1200000, 1800000);
        var moved = HistorySelection.Move(domain, range, -86400000);
        Check(moved.From == 0 && moved.To == 600000, "overview pan clamps left while preserving width");
        moved = HistorySelection.Move(domain, range, 86400000);
        Check(moved.From == 85800000 && moved.To == domain.To, "overview pan clamps right while preserving width");
        moved = HistorySelection.Move(domain, range, long.MaxValue);
        Check(moved.From == 85800000 && moved.To == domain.To, "extreme positive pan clamps right without overflowing left");
        moved = HistorySelection.Move(domain, range, long.MinValue);
        Check(moved.From == domain.From && moved.To == 600000, "extreme negative pan clamps left while preserving width");
        foreach (long origin in new[] { long.MinValue, long.MaxValue - 1000000 }) {
            var edgeDomain = new TimeWindow(origin, origin + 1000000);
            var edgeSelection = new TimeWindow(origin + 200000, origin + 800000);
            var right = HistorySelection.Move(edgeDomain, edgeSelection, long.MaxValue);
            var left = HistorySelection.Move(edgeDomain, edgeSelection, long.MinValue);
            Check(right.From == origin + 400000 && right.To == edgeDomain.To && left.From == origin && left.To == origin + 600000,
                "pan preserves direction and width near integer timestamp limit " + origin);
        }
        var resized = HistorySelection.Resize(domain, range, true, 3600000);
        Check(resized.From == range.To - 60000 && resized.To == range.To, "left range handle cannot cross right handle");
        resized = HistorySelection.Resize(domain, range, false, 0);
        Check(resized.From == range.From && resized.To == range.From + 60000, "right range handle cannot cross left handle");
        resized = HistorySelection.Resize(domain, range, false, long.MaxValue);
        Check(resized.To == domain.To && resized.From == range.From, "range zoom clamps to retained data");
        var shortened = HistorySelection.Clamp(new TimeWindow(1500000, 1700000), range);
        Check(shortened.From == 1500000 && shortened.To == 1700000, "retention shrinking the domain clamps both selection boundaries");
        var single = HistorySelection.Clamp(new TimeWindow(1000, 1001), range);
        Check(single.From == 1000 && single.To == 1001, "single reading selection remains valid");
        var oversized = HistorySelection.Clamp(domain, new TimeWindow(-60000, 90000000));
        Check(oversized.From == 0 && oversized.To == domain.To, "all-data preset covers available domain");
        var points = new List<ChartPoint>();
        for (int i = 0; i <= 3600; i++) points.Add(new ChartPoint { UnixMs = i * 1000L, Bpm = 60 + i % 20, Min = 50, Max = 100 });
        var preview = HistorySelection.Preview(points, new TimeWindow(600000, 1200000), 100);
        Check(preview.Count <= 100 && preview.All(p => p.UnixMs >= 600000 && p.UnixMs < 1200000), "drag preview is bounded and respects half-open selected range");
        Check(preview.All(p => p.Min == 50 && p.Max == 100 && p.Bpm >= 60 && p.Bpm <= 79), "drag preview preserves recorded extrema");
        Check(points[600].Bpm == 60 && points.Count == 3601, "drag preview leaves cached measurements unchanged");
        Check(HistorySelection.Preview(points, new TimeWindow(4000000, 4600000)).Count == 0, "empty selected period never fabricates points");
    }

    private static void TestChartAggregationBoundaries(string folder)
    {
        var range = new TimeWindow(0, 2000);
        var source = new List<ChartPoint> {
            new ChartPoint { UnixMs = 0, Bpm = 60, Min = 50, Max = 70 },
            new ChartPoint { UnixMs = 0, Bpm = 100, Min = 90, Max = 110 },
            new ChartPoint { UnixMs = 1000, Bpm = 80, Min = 70, Max = 90 },
            new ChartPoint { UnixMs = 2000, Bpm = 999, Min = 999, Max = 999 }
        };
        var preview = HistorySelection.Preview(source, range, 2);
        Check(preview.Count == 2 && preview[0].UnixMs == 0 && preview[0].Bpm == 80 && preview[0].Min == 50 && preview[0].Max == 110 && preview[1].Bpm == 80,
            "preview combines duplicate timestamps, preserves extrema and excludes the end boundary");
        foreach (int maximum in new[] { 0, -1, 14401, int.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => HistorySelection.Preview(source, range, maximum), "preview rejects invalid bucket limits: " + maximum);
        Throws<ArgumentException>(() => HistorySelection.Preview(new List<ChartPoint> { source[0], source[2], source[1] }, range, 2),
            "preview rejects out-of-order points instead of exceeding its bucket limit");
        Throws<ArgumentNullException>(() => HistorySelection.Preview(null, range), "preview rejects a missing source explicitly");
        Throws<ArgumentNullException>(() => HistorySelection.Preview(source, null), "preview rejects a missing range explicitly");
        Throws<ArgumentException>(() => HistorySelection.Preview(new List<ChartPoint> { null }, range), "preview rejects a missing point explicitly");
        Throws<OverflowException>(() => HistorySelection.Preview(source, new TimeWindow(long.MinValue, long.MaxValue)), "preview rejects intervals with an unrepresentable width");

        var wide = new TimeWindow(0, long.MaxValue);
        var widePoints = new List<ChartPoint> {
            new ChartPoint { UnixMs = 0, Bpm = 60, Min = 60, Max = 60 },
            new ChartPoint { UnixMs = long.MaxValue / 2, Bpm = 80, Min = 80, Max = 80 },
            new ChartPoint { UnixMs = long.MaxValue - 1, Bpm = 100, Min = 100, Max = 100 }
        };
        preview = HistorySelection.Preview(widePoints, wide, 2);
        Check(preview.Count == 2 && preview[0].Bpm == 70 && preview[1].Bpm == 100, "preview ceiling division does not overflow a representable wide interval");
        using (var store = new Store(Path.Combine(folder, "bucket-boundaries.db"))) {
            foreach (var point in widePoints) store.Add(new Reading { UnixMs = point.UnixMs, Bpm = (int)point.Bpm });
            var chart = store.Chart(wide.From, wide.To, 2);
            Check(chart.Count == 2 && chart[0].Bpm == 70 && chart[1].Bpm == 100, "SQLite and preview aggregation use the same bounded bucket width at integer limits");
            foreach (int maximum in new[] { 0, -1, 14401, int.MaxValue })
                Throws<ArgumentOutOfRangeException>(() => store.Chart(0, 2000, maximum), "SQLite chart rejects invalid bucket limits: " + maximum);
            Throws<OverflowException>(() => store.Chart(long.MinValue, long.MaxValue), "chart rejects intervals whose width cannot fit a signed timestamp");
        }
    }

    private static void TestChartInteraction()
    {
        var domain = new TimeWindow(0, 7200000); var view = new TimeWindow(1800000, 2400000);
        var zoom = HistorySelection.Zoom(domain, view, 120, 0.5);
        Check(zoom.To - zoom.From == 480000 && zoom.From + (zoom.To - zoom.From) / 2 == 2100000, "wheel zoom narrows range around cursor timestamp");
        Check(HistorySelection.Zoom(domain, view, 120, 0).From == view.From && HistorySelection.Zoom(domain, view, 120, 1).To == view.To, "wheel zoom preserves left and right cursor anchors");
        zoom = HistorySelection.Zoom(domain, view, -120, 0.5);
        Check(zoom.To - zoom.From == 750000, "reverse wheel broadens range");
        zoom = HistorySelection.Zoom(domain, view, 120000, 0.5);
        Check(zoom.To - zoom.From == 60000, "extreme zoom in clamps to one minute");
        zoom = HistorySelection.Zoom(domain, view, -120000, 0.5);
        Check(zoom.From == domain.From && zoom.To == domain.To, "extreme zoom out clamps to retained domain");
        var minimum = new TimeWindow(100000, 160000); zoom = HistorySelection.Zoom(domain, minimum, 120, 0.5);
        Check(zoom.From == minimum.From && zoom.To == minimum.To, "minimum zoom does not move selected window");
        var selected = HistorySelection.SelectedInterval(view, 0.8, 0.2);
        Check(selected.From == 1920000 && selected.To == 2280000, "reverse chart drag selects ordered timestamp interval");
        Check(HistorySelection.SelectedInterval(view, 0.5, 0.5) == null, "click without interval never fabricates a selection");
        selected = HistorySelection.SelectedInterval(view, -1, 2);
        Check(selected.From == view.From && selected.To == view.To, "chart drag clamps outside pointer positions to visible bounds");
        var samples = new List<ChartPoint> {
            new ChartPoint { UnixMs=0, Bpm=60, Min=60, Max=60 }, new ChartPoint { UnixMs=10, Bpm=90, Min=90, Max=90 },
            new ChartPoint { UnixMs=15, Bpm=0, Min=0, Max=0 }, new ChartPoint { UnixMs=20, Bpm=120, Min=120, Max=120 }
        };
        var summary = ChartAnalysis.SummarizeRaw(samples, new TimeWindow(0,20));
        Check(summary.Count == 2 && summary.Average == 75 && summary.Min == 60 && summary.Max == 90, "interval stats use original valid samples and half-open range");
        summary = ChartAnalysis.SummarizeRaw(samples, new TimeWindow(30,40));
        Check(summary.Count == 0 && summary.Average == 0, "empty analysis interval has no invented average");
        Throws(() => ChartAnalysis.SummarizeRaw(new[] { new ChartPoint { UnixMs=0, Bpm=75, Min=60, Max=90 } }, new TimeWindow(0,10)), "interval stats reject averaged chart buckets");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, (double)int.MaxValue + 1, double.MaxValue })
            Throws<ArgumentException>(() => ChartAnalysis.SummarizeRaw(new[] { new ChartPoint { UnixMs = 0, Bpm = invalid, Min = invalid, Max = invalid } }, new TimeWindow(0, 10)),
                "interval stats reject values that cannot be original integer readings: " + invalid);
        summary = ChartAnalysis.SummarizeRaw(new[] {
            new ChartPoint { UnixMs = 0, Bpm = int.MaxValue, Min = int.MaxValue, Max = int.MaxValue },
            new ChartPoint { UnixMs = 1, Bpm = int.MaxValue, Min = int.MaxValue, Max = int.MaxValue }
        }, new TimeWindow(0, 10));
        Check(summary.Count == 2 && summary.Min == int.MaxValue && summary.Max == int.MaxValue && summary.Average == int.MaxValue,
            "interval stats preserve the full integer reading range without sum overflow");
    }

    private static void TestChartTimeTicks()
    {
        var origin = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long aligned = origin.ToUnixTimeMilliseconds() - (long)TimeZoneInfo.Local.GetUtcOffset(origin).TotalMilliseconds;
        Check(ChartTime.Ticks(aligned, aligned + 600000, 800).SequenceEqual(Enumerable.Range(0, 6).Select(i => aligned + i * 120000L)),
            "chart ticks retain exact local grid alignment for a ten-minute window");
        foreach (double pixels in new[] { 0.0, 100.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check(ChartTime.Ticks(aligned, aligned + 600000, pixels).SequenceEqual(new[] { aligned, aligned + 600000 }),
                "chart retains endpoints when interior ticks cannot fit: " + pixels);
        foreach (long span in new[] { 1L, 60000L, 86400000L, 3153600000000L }) {
            var ticks = ChartTime.Ticks(aligned, aligned + span, 800);
            Check(ticks.First() == aligned && ticks.Last() == aligned + span && ticks.SequenceEqual(ticks.Distinct().OrderBy(t => t)),
                "chart tick endpoints remain exact and ordered across span " + span);
            Check(ticks.Count <= 9 && ticks.Skip(1).Take(ticks.Count - 2).All(t => (t - aligned) * 800.0 / span >= 95 && (aligned + span - t) * 800.0 / span >= 95),
                "chart interior ticks respect endpoint spacing and remain bounded across span " + span);
        }
        Check(ChartTime.Ticks(aligned, aligned, 800).SequenceEqual(new[] { aligned }), "empty chart range has a single starting tick");
    }

    private static void TestDailyActivity(string folder)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-jst", TimeSpan.FromHours(9), "test-jst", "test-jst");
        var first = new DateTime(2024, 2, 28);
        long midnight = DailyActivity.Midnight(first.AddDays(1), zone);
        using (var store = new Store(Path.Combine(folder, "activity.db"))) {
            foreach (long time in new[] { midnight - 1, midnight, midnight + 1, midnight + 86400000 })
                store.Add(new Reading { UnixMs = time, Bpm = 80, Session = "isolated", Device = "test" });
            store.Add(new Reading { UnixMs = midnight + 2, Bpm = 0 });
            store.Add(new Reading { UnixMs = midnight + 3, Bpm = 90, Contact = false });
            var days = store.DailyActivity(first, first.AddDays(3), zone);
            Check(days.Count == 4 && days[0].Count == 1 && days[1].Count == 2 && days[2].Count == 1 && days[3].Count == 0, "calendar groups local midnight boundaries, leap day and empty days without invalid samples");
            Check(days[1].Date == new DateTime(2024, 2, 29), "calendar preserves local leap-day date");
            Check(store.DailyActivity(first.AddDays(-364), first, zone).Count == 365, "calendar returns exactly 365 days including empty days");
            Throws(() => store.DailyActivity(first, first.AddDays(-1), zone), "calendar rejects reversed range");
            store.DeleteBefore(midnight + 1);
            Check(store.DailyActivity(first, first.AddDays(1), zone).Sum(d => d.Count) == 1, "calendar reflects retention deletion without cached counts");
        }
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var spring = new DateTime(2024, 3, 10); var autumn = new DateTime(2024, 11, 3);
        Check(DailyActivity.Midnight(spring.AddDays(1), pacific) - DailyActivity.Midnight(spring, pacific) == 23 * 3600000L, "calendar respects 23-hour daylight-saving day");
        Check(DailyActivity.Midnight(autumn.AddDays(1), pacific) - DailyActivity.Midnight(autumn, pacific) == 25 * 3600000L, "calendar respects 25-hour daylight-saving day");
        Check(new long[] { 0, 1, 599, 600, 3599, 3600, 10799, 10800 }.Select(DailyActivity.Level).SequenceEqual(new[] { 0, 1, 1, 2, 2, 3, 3, 4 }), "calendar color thresholds are fixed sample counts");
    }

    private static void TestMidnightTransitions(string folder)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 23, 59, 30), 3, 1);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 59, 30), 10, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("isolated-midnight-transition", TimeSpan.Zero, "test", "standard", "daylight", new[] { rule });
        var day = new DateTime(2024, 3, 2);
        var validLocal = day.AddMinutes(59).AddSeconds(30);
        Check(zone.IsInvalidTime(day) && zone.IsInvalidTime(validLocal.AddTicks(-1)) && !zone.IsInvalidTime(validLocal),
            "test time zone has an invalid midnight whose first valid instant falls between whole minutes");
        long boundary = new DateTimeOffset(validLocal, TimeSpan.FromHours(1)).ToUnixTimeMilliseconds();
        Check(DailyActivity.Midnight(day, zone) == boundary, "invalid midnight resolves to the exact first valid instant without skipping recorded seconds");
        using (var store = new Store(Path.Combine(folder, "midnight-transition.db"))) {
            foreach (long time in new[] { boundary - 1, boundary, boundary + 29999, boundary + 30000 })
                store.Add(new Reading { UnixMs = time, Bpm = 80, Session = "ISOLATED MIDNIGHT TEST" });
            var days = store.DailyActivity(day.AddDays(-1), day, zone);
            Check(days.Count == 2 && days[0].Count == 1 && days[1].Count == 3,
                "daily activity assigns every sample to the correct day around a non-minute midnight transition");
        }
        var repeated = new DateTime(2024, 10, 1);
        Check(zone.IsAmbiguousTime(repeated) && DailyActivity.Midnight(repeated, zone) == new DateTimeOffset(repeated, TimeSpan.FromHours(1)).ToUnixTimeMilliseconds(),
            "ambiguous midnight keeps the earlier occurrence so repeated local times are included once");
        Throws<ArgumentNullException>(() => DailyActivity.Midnight(day, null), "midnight conversion rejects a missing time zone explicitly");
    }

    private static void TestActivityCalendarUi()
    {
        RunOnSta(() => {
            var host = new System.Windows.Window();
            try {
                ThemeManager.Initialize(host); ThemeManager.Apply(host, "Dark");
                var calendar = new ActivityCalendar(); host.Content = calendar;
                var start = new DateTime(2025, 10, 25);
                var activity = Enumerable.Range(0, 365).Select(i => new DailyActivity { Date = start.AddDays(i), Count = i == 0 ? 600 : 0 }).ToList();
                calendar.SetDays(activity);
                var grid = calendar.Children.OfType<System.Windows.Controls.Grid>().Single();
                var buttons = grid.Children.OfType<System.Windows.Controls.Button>().ToList();
                Check(buttons.Count == 365 && buttons.Count(b => b.IsTabStop) == 1, "calendar exposes every day with a single keyboard tab entry");
                Check(System.Windows.Automation.AutomationProperties.GetAutomationId(buttons[0]) == "ActivityDay20251025", "calendar exposes exact dates to automation");
                Check(grid.Children.OfType<System.Windows.Controls.TextBlock>().Any(label => label.Text == "11月"), "short first month does not suppress the next month label");
                DateTime? selected = null; calendar.DaySelected += date => selected = date;
                buttons[0].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(selected == start, "calendar click navigates to the matching recorded date");
                buttons[1].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(selected == start.AddDays(1), "calendar empty-day click preserves the requested date");
                ThemeManager.Apply(host, "Light");
                Check(buttons[0].Background == host.Resources["Activity2"], "calendar day colors follow shared theme resources");
                calendar.SetError("isolated error");
                Check(grid.Children.Count == 0, "calendar read failure clears stale day values");
            } finally { host.Close(); }
        }, "Calendar UI verification failed");
    }

    private static void TestStoreBounds(string folder)
    {
        string database = Path.Combine(folder, "store-bounds.db");
        using (var store = new Store(database)) {
            Check(store.Bounds() == null, "empty store has no recording bounds");
            store.Add(new Reading { UnixMs = 5000, Bpm = 80 });
            var bounds = store.Bounds();
            Check(bounds.From == 5000 && bounds.To == 5001, "single reading bounds include its exact timestamp in a half-open interval");
            foreach (long time in new long[] { -1000, 5000, 3000, -1000 })
                store.Add(new Reading { UnixMs = time, Bpm = 0, Contact = false });
            bounds = store.Bounds();
            Check(bounds.From == -1000 && bounds.To == 5001,
                "bounds include non-heartbeat readings and duplicate endpoints regardless of insertion order");
            using (var reopened = new Store(database)) {
                bounds = reopened.Bounds();
                Check(bounds.From == -1000 && bounds.To == 5001, "recording bounds survive reopening the database");
            }
            store.DeleteBefore(2000);
            bounds = store.Bounds();
            Check(bounds.From == 3000 && bounds.To == 5001, "retention updates the earliest recording bound without changing the latest");
            store.DeleteBefore(5000);
            bounds = store.Bounds();
            Check(bounds.From == 5000 && bounds.To == 5001, "duplicate final timestamps still produce a one-millisecond recording interval");
            store.DeleteBefore(5001);
            Check(store.Bounds() == null, "removing every recording restores empty bounds");
        }
    }

    private static void TestHistoryRepository(string folder)
    {
        string database = Path.Combine(folder, "history-repository.db");
        var history = new HistoryRepository(database);
        var empty = history.LoadOverview();
        Check(empty.Bounds == null && empty.Preview.Count == 0 && empty.Overview.Count == 0,
            "empty history overview has no fabricated bounds or chart points");
        var range = new TimeWindow(1000, 4000);
        var detail = history.LoadWindow(range);
        Check(detail.Summary.Count == 0 && detail.Points.Count == 0, "empty history window has no samples");

        using (var store = new Store(database)) {
            store.Add(new Reading { UnixMs = 1000, Bpm = 60, Contact = true });
            store.Add(new Reading { UnixMs = 2000, Bpm = 100 });
            store.Add(new Reading { UnixMs = 2500, Bpm = 90, Contact = false });
            store.Add(new Reading { UnixMs = 3000, Bpm = 0 });
            store.Add(new Reading { UnixMs = 4000, Bpm = 200, Contact = true });
        }
        var overview = history.LoadOverview();
        Check(overview.Bounds.From == 1000 && overview.Bounds.To == 4001 && overview.Preview.Count == 3 && overview.Overview.Count == 3,
            "history overview reads current bounds and filters invalid chart samples");
        detail = history.LoadWindow(range);
        Check(detail.Summary.Count == 2 && detail.Summary.Average == 80 && detail.Summary.Min == 60 && detail.Summary.Max == 100 && detail.Points.Count == 2,
            "history detail preserves exact statistics and half-open chart boundaries");
        var analysis = history.Summarize(new TimeWindow(2000, 4000));
        Check(analysis.Count == 1 && analysis.Average == 100, "history analysis uses its own selected interval");
        Check(history.LoadActivity(new DateTime(1970, 1, 1), TimeZoneInfo.Utc).Last().Count == 3,
            "history activity includes only valid samples in the requested calendar day");
        string csv = Path.Combine(folder, "repository-export.csv");
        Check(history.Export(csv, range) == 4 && File.ReadAllLines(csv).Length == 5,
            "history CSV retains invalid samples while respecting the displayed interval");
        string intervalCsv = Path.Combine(folder, "analysis-export.csv");
        Check(history.Export(intervalCsv, new TimeWindow(2000, 3000)) == 2,
            "analysis CSV includes its start and no-contact samples but excludes its end");
        var fullLines = File.ReadAllLines(csv);
        var intervalLines = File.ReadAllLines(intervalCsv);
        Check(intervalLines.SequenceEqual(new[] { fullLines[0], fullLines[2], fullLines[3] }),
            "analysis CSV preserves the displayed export schema and original rows");
        Throws(() => history.Export(folder, range), "history export reports an inaccessible output target");
        Check(history.LoadWindow(range).Summary.Count == 2, "history queries remain usable after export failure");
        using (var store = new Store(database)) store.DeleteBefore(4000);
        Check(history.LoadOverview().Bounds.From == 4000 && history.LoadWindow(range).Summary.Count == 0,
            "history reload observes retention changes without stale cached records");
    }

    private static void TestHistoryReadSnapshot(string folder)
    {
        string database = Path.Combine(folder, "history-snapshot.db");
        using (var reader = new Store(database))
        using (var writer = new Store(database)) {
            writer.Add(new Reading { UnixMs = 1000, Bpm = 60 });
            writer.Add(new Reading { UnixMs = 2000, Bpm = 100 });
            var snapshot = reader.ReadSnapshot(() => {
                var bounds = reader.Bounds();
                var summary = reader.Summary(bounds.From, bounds.To);
                writer.DeleteBefore(3000);
                writer.Add(new Reading { UnixMs = 3000, Bpm = 120 });
                var points = reader.Chart(bounds.From, bounds.To);
                Check(bounds.From == 1000 && bounds.To == 2001 && summary.Count == 2 && summary.Average == 80 && points.Count == 2,
                    "history snapshot retains matching bounds, statistics and charts across external retention and recording");
                Check(reader.Query(0, 4000).Select(row => row.UnixMs).SequenceEqual(new long[] { 2000, 1000 }),
                    "history snapshot excludes new readings and retains rows deleted through another connection");
                Check(writer.Query(0, 4000).Single().UnixMs == 3000, "history read transaction allows another connection to commit updates");
                return new HistoryDetail(summary, points);
            });
            Check(snapshot.Summary.Count == 2 && snapshot.Points.Count == 2, "history snapshot returns complete results after committing its read transaction");
            Check(reader.Bounds().From == 3000 && reader.Query(0, 4000).Count == 1, "completed history snapshot releases the old view for subsequent reads");

            bool rejected = false;
            try {
                reader.ReadSnapshot<int>(() => {
                    reader.Query(0, 4000);
                    writer.Add(new Reading { UnixMs = 3500, Bpm = 80 });
                    throw new IOException("Isolated read failure");
                });
            } catch (IOException ex) { rejected = ex.Message == "Isolated read failure"; }
            Check(rejected, "history snapshot propagates the original read failure");
            Check(reader.Query(0, 4000).Count == 2 && reader.ReadSnapshot(() => reader.Summary(0, 4000)).Average == 100,
                "failed history snapshot rolls back and permits fresh reads and another snapshot");
        }
    }

    private static void TestSnapshotFailurePreservation(string folder)
    {
        string database = Path.Combine(folder, "snapshot-failure-preservation.db");
        var original = new IOException("Isolated read failure while the connection is closing");
        Exception observed = null;
        using (var store = new Store(database)) {
            store.Add(new Reading { UnixMs = 1000, Bpm = 80 });
            try {
                store.ReadSnapshot<int>(() => {
                    store.Query(0, 2000);
                    store.Dispose();
                    throw original;
                });
            } catch (Exception ex) { observed = ex; }
        }
        Check(ReferenceEquals(observed, original), "snapshot preserves the original read failure when its connection was already closed");
        using (var store = new Store(database)) {
            Check(store.Query(0, 2000).Single().Bpm == 80, "closing a failed read snapshot preserves committed readings and releases the database");
            Throws<ArgumentNullException>(() => store.ReadSnapshot<int>(null), "snapshot rejects a missing reader before starting a transaction");
            observed = null;
            try { store.ReadSnapshot<int>(() => { store.Query(0, 2000); throw original; }); }
            catch (Exception ex) { observed = ex; }
            Check(ReferenceEquals(observed, original), "successful snapshot rollback preserves the exact original exception");
            Check(store.ReadSnapshot(() => store.Summary(0, 2000)).Count == 1, "snapshot connection remains usable after ordinary rollback and rejected input");
        }
    }

    private static void TestCsvCultureInvariance(string folder)
    {
        var instant = new DateTimeOffset(2024, 5, 6, 12, 34, 56, TimeSpan.Zero).AddMilliseconds(789);
        var row = new Reading { UnixMs = instant.ToUnixTimeMilliseconds(), Bpm = 80, Contact = true, Rr = "750.5", Energy = 42, Device = "sensor", Session = "culture-test" };
        string expectedLocal = instant.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        string expectedRow = "\"" + instant.ToString("O", CultureInfo.InvariantCulture) + "\",\"" + expectedLocal + "\",80,1,\"750.5\",42,\"sensor\",\"culture-test\"";
        var japaneseEra = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone();
        japaneseEra.DateTimeFormat.Calendar = new JapaneseCalendar();
        var separator = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        separator.DateTimeFormat.TimeSeparator = ".";
        var original = Thread.CurrentThread.CurrentCulture;
        using (var store = new Store(Path.Combine(folder, "csv-cultures.db"))) {
            store.Add(row);
            try {
                foreach (var culture in new[] { CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("th-TH"), japaneseEra, separator }) {
                    Thread.CurrentThread.CurrentCulture = culture;
                    string context = culture.Name + " / " + culture.DateTimeFormat.Calendar.GetType().Name + " / " + culture.DateTimeFormat.TimeSeparator;
                    Check(row.Time == expectedLocal, "persisted local timestamp uses a stable Gregorian format under " + context);
                    string csv = Path.Combine(folder, "csv-culture.csv");
                    long count = store.Export(csv, row.UnixMs, row.UnixMs + 1);
                    var lines = File.ReadAllLines(csv, Encoding.UTF8);
                    Check(count == 1 && lines.Length == 2 && lines[1] == expectedRow, "complete CSV row is independent of the current culture under " + context);
                }
            } finally { Thread.CurrentThread.CurrentCulture = original; }
        }
    }

    private static void TestCsvExportFailure(string folder)
    {
        string directory = Path.Combine(folder, "atomic-export");
        Directory.CreateDirectory(directory);
        string csv = Path.Combine(directory, "history.csv");
        byte[] original = Encoding.UTF8.GetBytes("existing export\r\n");
        string database = Path.Combine(folder, "atomic-export.db");
        using (var store = new Store(database)) {
            store.Add(new Reading { UnixMs = 1000, Bpm = 60, Device = "quoted,\"sensor\"" });
            // A persisted timestamp outside DateTimeOffset's range fails after the first CSV row.
            store.Add(new Reading { UnixMs = long.MaxValue - 1, Bpm = 80 });
            File.WriteAllBytes(csv, original);
            Throws(() => store.Export(csv, 0, long.MaxValue), "CSV reports a mid-export conversion failure");
            Check(File.ReadAllBytes(csv).SequenceEqual(original), "failed CSV export preserves the existing destination byte for byte");
            string missing = Path.Combine(directory, "new.csv");
            Throws(() => store.Export(missing, 0, long.MaxValue), "new CSV reports a mid-export conversion failure");
            Check(!File.Exists(missing), "failed CSV export does not publish a partial new destination");
            using (var locked = new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.None))
                Throws(() => store.Export(csv, 0, 2000), "CSV reports a locked destination");
            Check(File.ReadAllBytes(csv).SequenceEqual(original), "failed CSV replacement preserves the locked destination");
            Check(store.Export(csv, 0, 2000) == 1 && File.ReadAllText(csv).Contains("\"quoted,\"\"sensor\"\"\""),
                "CSV replaces the destination with complete escaped data after a failure");
            Check(store.Export(missing, 0, 1) == 0 && File.ReadAllLines(missing).Length == 1,
                "empty CSV export publishes a header-only file");
            Check(Directory.GetFiles(directory).Length == 2, "successful and failed CSV exports clean up temporary files");
            foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
                Throws(() => store.Export(database + suffix, 0, 2000), "CSV rejects database storage target: " + suffix);
            Throws(() => store.Export(Path.Combine(folder, ".", "ATOMIC-EXPORT.DB"), 0, 2000),
                "CSV database protection normalizes paths and ignores Windows filename case");
            Check(store.Query(0, 2000).Count == 1, "database remains readable after rejected exports");
        }
        using (var reopened = new Store(database))
            Check(reopened.Query(0, 2000).Single().Bpm == 60, "rejected CSV targets preserve the database across reopen");
    }

    private static void TestStoreInputBoundaries(string folder)
    {
        Throws<IOException>(() => new Store(folder), "SQLite open failure reports an inaccessible directory target");
        string invalidDatabase = Path.Combine(folder, "invalid-sqlite.db");
        byte[] marker = Encoding.UTF8.GetBytes("ISOLATED INVALID SQLITE DATA");
        File.WriteAllBytes(invalidDatabase, marker);
        Throws<IOException>(() => new Store(invalidDatabase), "SQLite initialization rejects a non-database file");
        using (var exclusive = new FileStream(invalidDatabase, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(exclusive.Length == marker.Length, "failed SQLite initialization releases its file handle without truncating the input");
        File.WriteAllBytes(invalidDatabase, new byte[0]);
        using (var recovered = new Store(invalidDatabase)) {
            recovered.Add(new Reading { UnixMs = 1000, Bpm = 90 });
            Check(recovered.Query(0, 2000).Single().Bpm == 90, "SQLite connection can be reopened at the same path after an initialization failure");
        }
        using (var store = new Store(Path.Combine(folder, "store-input-boundaries.db"))) {
            var first = new Reading { UnixMs = 1000, Bpm = 70 };
            var second = new Reading { UnixMs = 1000, Bpm = 80 };
            store.Add(first); store.Add(second);
            foreach (int invalid in new[] { -1, int.MinValue })
                Throws<ArgumentOutOfRangeException>(() => store.Query(0, 2000, invalid), "negative query limits never enable an unbounded SQLite read: " + invalid);
            Check(store.Query(0, 2000, 0).Count == 0, "zero query limit returns no rows");
            Check(store.Query(0, 2000, 1).Single().Id == second.Id, "limited queries use insertion order to break equal timestamp ties");
            Throws<ArgumentNullException>(() => store.Add(null), "store rejects null readings before preparing an insert");
            store.Add(new Reading { UnixMs = 1500, Bpm = 90 });
            Check(store.Query(0, 2000).Count == 3, "store continues recording after rejected input without partial inserts");
        }
    }

    private static void TestStoreLifetime(string folder)
    {
        var disposed = new Store(Path.Combine(folder, "disposed.db"));
        disposed.Dispose();
        disposed.Dispose();
        try { disposed.Query(0, 2000); throw new Exception("Disposed store was queried."); }
        catch (ObjectDisposedException) { Check(true, "disposed store rejects queries before entering SQLite"); }
    }

    private static void TestStoreTextRoundTrip(string folder)
    {
        string database = Path.Combine(folder, "text-roundtrip.db");
        string[] values = { "", "before\0after", "\0leading\0", "日本語\r\n\"quoted\",\U0001F493", new string('字', 8192) };
        using (var store = new Store(database)) {
            for (int i = 0; i < values.Length; i++)
                store.Add(new Reading { UnixMs = i + 1, Bpm = 72, Session = values[i], Device = values[i], Rr = values[i] });
        }
        using (var reopened = new Store(database)) {
            for (int i = 0; i < values.Length; i++) {
                var row = reopened.Query(i + 1, i + 2).Single();
                Check(row.Session == values[i] && row.Device == values[i] && row.Rr == values[i],
                    "SQLite text roundtrip preserves exact UTF-8 column contents: case " + i);
            }
            string csv = Path.Combine(folder, "text-roundtrip.csv");
            Check(reopened.Export(csv, 0, values.Length + 1) == values.Length, "CSV exports every text roundtrip sample");
            string content = File.ReadAllText(csv, Encoding.UTF8);
            Check(values.All(value => content.Contains("\"" + value.Replace("\"", "\"\"") + "\"")),
                "CSV preserves embedded NUL, Unicode, newlines, quotes and long text fields");
        }
    }

    private static void TestStorePersistence(string folder)
    {
        string database = Path.Combine(folder, "心拍.db");
        using (var store = new Store(database)) {
            store.Add(new Reading { UnixMs = 1000, Bpm = 70, Device = "COOSPO,\"日本語\"", Session = "s1", Contact = true, Rr = "1000;500", Energy = 42 });
            store.Add(new Reading { UnixMs = 2000, Bpm = 90, Device = "HW706", Session = "s1", Contact = null, Rr = "" });
            store.Add(new Reading { UnixMs = 3000, Bpm = 0, Device = "HW706", Session = "s1", Contact = false, Rr = "" });
            store.Add(new Reading { UnixMs = 4000, Bpm = 300, Device = "HW706", Session = "s1", Contact = true, Rr = "" });
            var rows = store.Query(1000, 3000); Check(rows.Count == 2 && rows[0].Bpm == 90 && rows[1].Energy == 42, "half-open history range, descending order, optional fields");
            Check(rows[1].Device == "COOSPO,\"日本語\"" && rows[1].Contact == true && rows[0].Contact == null, "UTF-8 and nullable contact roundtrip");
            var summary = store.Summary(1000, 4000); Check(summary.Count == 2 && summary.Min == 70 && summary.Max == 90 && summary.Average == 80, "statistics exclude zero / absent contact");
            Check(store.Query(0, 5000, 1).Single().Bpm == 300, "raw heart rate is not clamped in storage");
            Check(store.Chart(0, 5000).Count == 3, "history chart excludes invalid readings");
            string csv = Path.Combine(folder, "history.csv"); Check(store.Export(csv, 1000, 3000) == 2, "CSV exports exact selected range");
            Check(File.ReadAllText(csv).Contains("\"COOSPO,\"\"日本語\"\"\""), "CSV quotes delimiters and Unicode");
        }
        using (var reopened = new Store(database)) Check(reopened.Query(0, 5000).Count == 4, "SQLite persists across reopen");
    }
}
