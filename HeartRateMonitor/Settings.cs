using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using Windows.Devices.Bluetooth;

namespace HeartRateMonitor
{
    public sealed class Settings
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 9000;
        public string IntParameter { get; set; } = "VRCOSC/Heartrate/Value";
        public string FloatParameter { get; set; } = "HRPercent";
        public int PercentMin { get; set; } = 0;
        public int PercentMax { get; set; } = 200;
        public bool OscEnabled { get; set; } = true;
        public bool OscZeroOnTimeout { get; set; } = true;
        public int OscTimeoutSeconds { get; set; } = 5;
        public int OscTimeoutInt { get; set; } = 0;
        public float OscTimeoutFloat { get; set; } = 0;
        public bool AutoReconnect { get; set; } = true;
        public bool AutoConnect { get; set; } = true;
        public string Theme { get; set; } = "System";
        public string Language { get; set; } = "ja";
        public bool CloseToTray { get; set; } = true;
        public bool OyasumiEnabled { get; set; } = true;
        // Zero automatically finds local listeners owned by OyasumiVR.
        public int OyasumiQueryPort { get; set; } = 0;
        public int SleepDayBoundaryHour { get; set; } = 0;
        // Zero means unlimited; absent fields in older settings also default to unlimited.
        public int RetentionDays { get; set; } = 0;
        internal static readonly int[] RetentionDayOptions = { 0, 1, 7, 30, 90, 365 };
        internal const long MillisecondsPerDay = 86400000L;
        public long? RetentionCutoff(long nowUnixMs) => RetentionDays == 0 ? (long?)null : nowUnixMs - RetentionDays * MillisecondsPerDay;
        public ulong LastAddress { get; set; }
        public string LastName { get; set; } = "";
        public bool LastDeviceVerified { get; set; }
        public string LastDeviceId { get; set; } = "";
        public int LastAddressType { get; set; } = -1;

        public static string Address(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(Words.T("パラメーター名を入力してください。", "Enter a parameter name."));
            string value = name.Trim();
            if (!value.StartsWith("/")) value = "/avatar/parameters/" + value;
            if (!value.StartsWith("/avatar/parameters/", StringComparison.Ordinal) || value.Length <= 19 ||
                value.Any(c => c < 33 || c > 126 || "#*,?[]{}".Contains(c)) || value.Contains("//") || value.EndsWith("/"))
                throw new ArgumentException(Words.T("OSCは /avatar/parameters/ 以下のASCII名を指定してください（空白・ワイルドカード不可）。", "Use an ASCII name under /avatar/parameters/ without spaces or wildcards."));
            return value;
        }
        public void Validate()
        {
            if (SleepDayBoundaryHour < 0 || SleepDayBoundaryHour > 23)
                throw new ArgumentException(Words.T("睡眠の一日の区切りは0〜23時で指定してください。", "Sleep day boundary must be between 0 and 23 hours."));
            if (OyasumiQueryPort < 0 || OyasumiQueryPort > 65535)
                throw new ArgumentException(Words.T("OyasumiVRのOSCQueryポートは0〜65535で指定してください（0は自動検出）。", "OyasumiVR OSCQuery port must be 0–65535 (0 for automatic discovery)."));
            if (LastAddress > 0xFFFFFFFFFFFFUL) throw new ArgumentException(Words.T("前回のBluetoothアドレスが不正です。", "The saved Bluetooth address must fit 48 bits."));
            if (LastAddressType != -1 && !Enum.IsDefined(typeof(BluetoothAddressType), LastAddressType))
                throw new ArgumentException(Words.T("前回のBluetoothアドレス種別が不正です。", "The saved Bluetooth address type is invalid."));
            if (OscTimeoutSeconds < 1 || OscTimeoutSeconds > 3600) throw new ArgumentException(Words.T("OSCの無受信時間は1〜3600秒で指定してください。", "OSC receive timeout must be between 1 and 3600 seconds."));
            if (OscTimeoutInt < 0 || OscTimeoutInt > 255) throw new ArgumentException(Words.T("無受信時のInt代替値は0〜255で指定してください。", "Timeout Int fallback must be between 0 and 255."));
            if (float.IsNaN(OscTimeoutFloat) || float.IsInfinity(OscTimeoutFloat) || OscTimeoutFloat < 0 || OscTimeoutFloat > 1)
                throw new ArgumentException(Words.T("無受信時のFloat代替値は0.0〜1.0で指定してください。", "Timeout Float fallback must be between 0.0 and 1.0."));
            if (!RetentionDayOptions.Contains(RetentionDays)) throw new ArgumentException(Words.T("保存期間を選択してください。", "Choose a retention period."));
            if (Language != "ja" && Language != "en") throw new ArgumentException("Language must be ja or en.");
            if (Theme != "System" && Theme != "Dark" && Theme != "Light") throw new ArgumentException(Words.T("テーマを選択してください。", "Choose a theme."));
            IPAddress ip;
            if (!IPAddress.TryParse(Host, out ip)) throw new ArgumentException(Words.T("送信先にはIPアドレスを入力してください（例: 127.0.0.1）。", "Enter a destination IP address, such as 127.0.0.1."));
            if (Port < 1 || Port > 65535) throw new ArgumentException(Words.T("ポートは1〜65535です。", "Port must be between 1 and 65535."));
            if (PercentMin < 0 || PercentMax > 65535 || PercentMax <= PercentMin) throw new ArgumentException(Words.T("変換範囲は 0 ≦ 最小 < 最大 ≦ 65535 にしてください。", "Use a conversion range of 0 <= minimum < maximum <= 65535."));
            if (Address(IntParameter) == Address(FloatParameter)) throw new ArgumentException(Words.T("IntとFloatには別のパラメーター名を指定してください。", "Use distinct names for the Int and Float parameters."));
        }
        public float Percent(int bpm) => (float)Math.Max(0, Math.Min(1, ((double)bpm - PercentMin) / (PercentMax - PercentMin)));
        public Settings Copy() => (Settings)MemberwiseClone();
        public static Settings Load(string path)
        {
            string json;
            try { json = ReadJson(path); }
            catch (FileNotFoundException) { return new Settings(); }
            catch (DirectoryNotFoundException) { return new Settings(); }
            var value = new JavaScriptSerializer().Deserialize<Settings>(json);
            if (value == null) throw new FormatException(Words.T("設定ファイルが空です。", "The settings file is empty."));
            value.Validate(); return value;
        }
        private static string ReadJson(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var utf8 = new UTF8Encoding(true, true);
            // Check UTF-32 before UTF-16 because their little-endian preambles overlap.
            // Every recognized encoding rejects malformed input instead of replacing metadata.
            Encoding[] encodings = {
                new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
                new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), utf8
            };
            foreach (var encoding in encodings) {
                byte[] preamble = encoding.GetPreamble();
                if (bytes.Take(preamble.Length).SequenceEqual(preamble))
                    return encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
            }
            return utf8.GetString(bytes);
        }
        public void Save(string path)
        {
            Validate();
            string destination = Path.GetFullPath(path);
            string json = new JavaScriptSerializer().Serialize(this);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            AtomicFile.Write(destination, stream => {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)) writer.Write(json);
            }, keepBackup: true);
        }
    }
}
