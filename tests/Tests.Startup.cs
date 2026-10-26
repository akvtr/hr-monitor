using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeartRateMonitor;

internal static partial class Tests
{
    private static void TestInstanceNotification()
    {
        string key = "ISOLATED-TEST-" + Guid.NewGuid().ToString("N");
        Check(!InstanceNotification.TryRestore(key, 0), "restore notification reports a missing event without an unbounded wait");
        Throws<ArgumentOutOfRangeException>(() => InstanceNotification.TryRestore(key, -1), "restore notification rejects a negative wait timeout");
        using (var ready = new ManualResetEventSlim())
        {
            Task<bool> pending = Task.Run(() => { ready.Set(); return InstanceNotification.TryRestore(key, 2000); });
            Check(ready.Wait(1000), "second-instance notification starts before the primary creates its event");
            Thread.Sleep(50);
            Check(!pending.IsCompleted, "restore request waits while the primary event is not available yet");
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceNotification.EventName(key))) {
                Check(pending.Wait(3000) && pending.Result && signal.WaitOne(1000), "restore notification reaches an event created during primary startup");
                Check(!signal.WaitOne(0), "restore notification is consumed once by the primary");
                Check(InstanceNotification.TryRestore(key, 0) && signal.WaitOne(1000), "ready primary receives a restore request without waiting");
            }
        }
        Check(!InstanceNotification.TryRestore(key, 25), "restore notification times out when the primary event has been disposed");
    }

    private static void TestStartupOptions(string folder)
    {
        var normal = StartupOptions.Parse(new string[0]);
        Check(Path.IsPathRooted(normal.DataDirectory) && !normal.SmokeTest && !normal.Compact, "normal startup uses the default absolute data directory without test flags");
        string directory = Path.Combine(folder, "startup-options");
        var expected = StartupOptions.Parse(new[] { "--data-dir", directory });
        foreach (string variant in new[] { directory, directory + "\\", directory + "/", Path.Combine(directory, "."), directory.ToUpperInvariant() }) {
            var options = StartupOptions.Parse(new[] { "--data-dir", variant });
            Check(options.InstanceKey == expected.InstanceKey && string.Equals(options.DataDirectory, expected.DataDirectory, StringComparison.OrdinalIgnoreCase),
                "startup shares one instance identity across equivalent directory spelling: " + variant);
        }
        var root = StartupOptions.Parse(new[] { "--data-dir", Path.GetPathRoot(directory) });
        Check(root.DataDirectory == Path.GetPathRoot(directory), "startup normalization preserves the volume root separator");
        var smoke = StartupOptions.Parse(new[] { "--compact", "--smoke-test", "--data-dir", directory });
        Check(smoke.SmokeTest && smoke.Compact && smoke.DataDirectory == expected.DataDirectory, "startup parses test flags independently of argument order");
        foreach (var invalid in new[] {
            new[] { "--data-dir" }, new[] { "--data-dir", "" }, new[] { "--data-dir", (string)null },
            new[] { "--data-dir", "--smoke-test" }, new[] { "--unknown" },
            new[] { "--data-dir", directory, "--data-dir", directory }, new[] { "--smoke-test" },
            new[] { "--smoke-test", "--data-dir", normal.DataDirectory + "\\" }
        }) Throws<ArgumentException>(() => StartupOptions.Parse(invalid), "invalid startup arguments are rejected: " + string.Join(" ", invalid));
        Check(!Directory.Exists(directory), "startup option validation never creates directories or opens a database");

        string blocker = Path.Combine(folder, "not-a-directory.txt");
        File.WriteAllText(blocker, "preserve this file");
        foreach (string arguments in new[] { "--smoke-test --data-dir", "--smoke-test --data-dir \"" + blocker + "\"" }) {
            var info = new ProcessStartInfo(typeof(App).Assembly.Location, arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            using (var process = Process.Start(info)) {
                Check(process.WaitForExit(5000) && process.ExitCode == 1, "invalid smoke startup exits cleanly without opening the normal profile: " + arguments);
                Check(!string.IsNullOrWhiteSpace(process.StandardError.ReadToEnd()), "smoke startup errors remain observable even when error.txt cannot be written");
            }
        }
        Check(File.ReadAllText(blocker) == "preserve this file", "failed smoke startup preserves the invalid data-directory target");
    }
}
