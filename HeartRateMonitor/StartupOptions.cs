using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HeartRateMonitor
{
    internal sealed class StartupOptions
    {
        internal string DataDirectory { get; }
        internal string InstanceKey { get; }
        internal bool SmokeTest { get; }
        internal bool Compact { get; }

        private StartupOptions(string directory, bool smokeTest, bool compact)
        {
            DataDirectory = directory; SmokeTest = smokeTest; Compact = compact;
            using (var sha = SHA256.Create())
                InstanceKey = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()))).Replace("-", "");
        }

        internal static StartupOptions Parse(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            string normal = NormalizeDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeartRateMonitor"));
            string directory = normal;
            bool explicitDirectory = false, smokeTest = false, compact = false;
            for (int i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--data-dir":
                        if (explicitDirectory) throw new ArgumentException(Words.T("--data-dir は一度だけ指定してください。", "Specify --data-dir only once."));
                        if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                            throw new ArgumentException(Words.T("--data-dir の後に保存先を指定してください。", "--data-dir requires a directory path."));
                        directory = NormalizeDirectory(args[i]); explicitDirectory = true;
                        break;
                    case "--smoke-test": smokeTest = true; break;
                    case "--compact": compact = true; break;
                    default: throw new ArgumentException(Words.T("不明な引数: ", "Unknown argument: ") + args[i]);
                }
            }
            if (smokeTest && (!explicitDirectory || string.Equals(directory, normal, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(Words.T("スモークテストには --data-dir で通常と異なる保存先を指定してください。", "Smoke tests require --data-dir pointing to an isolated directory."));
            return new StartupOptions(directory, smokeTest, compact);
        }

        private static string NormalizeDirectory(string path)
        {
            string full = Path.GetFullPath(path);
            int rootLength = Path.GetPathRoot(full).Length;
            int length = full.Length;
            while (length > rootLength && (full[length - 1] == Path.DirectorySeparatorChar || full[length - 1] == Path.AltDirectorySeparatorChar)) length--;
            return full.Substring(0, length);
        }
    }
}
