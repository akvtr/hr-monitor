using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

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
        public bool AutoReconnect { get; set; } = true;
        public bool AutoConnect { get; set; } = true;
        public string Theme { get; set; } = "System";
        public string Language { get; set; } = "ja";
        public bool CloseToTray { get; set; } = true;
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
            if (OscTimeoutSeconds < 1 || OscTimeoutSeconds > 3600) throw new ArgumentException(Words.T("OSCの無受信時間は1〜3600秒で指定してください。", "OSC receive timeout must be between 1 and 3600 seconds."));
            if (!RetentionDayOptions.Contains(RetentionDays)) throw new ArgumentException(Words.T("保存期間を選択してください。", "Choose a retention period."));
            if (Language != "ja" && Language != "en") throw new ArgumentException("Language must be ja or en.");
            if (Theme != "System" && Theme != "Dark" && Theme != "Light") throw new ArgumentException(Words.T("テーマを選択してください。", "Choose a theme."));
            IPAddress ip;
            if (!IPAddress.TryParse(Host, out ip)) throw new ArgumentException(Words.T("送信先にはIPアドレスを入力してください（例: 127.0.0.1）。", "Enter a destination IP address, such as 127.0.0.1."));
            if (Port < 1 || Port > 65535) throw new ArgumentException(Words.T("ポートは1〜65535です。", "Port must be between 1 and 65535."));
            if (PercentMin < 0 || PercentMax > 65535 || PercentMax <= PercentMin) throw new ArgumentException(Words.T("変換範囲は 0 ≦ 最小 < 最大 ≦ 65535 にしてください。", "Use a conversion range of 0 <= minimum < maximum <= 65535."));
            if (Address(IntParameter) == Address(FloatParameter)) throw new ArgumentException(Words.T("IntとFloatには別のパラメーター名を指定してください。", "Use distinct names for the Int and Float parameters."));
        }
        public float Percent(int bpm) => (float)Math.Max(0, Math.Min(1, (bpm - PercentMin) / (double)(PercentMax - PercentMin)));
        public Settings Copy() => (Settings)MemberwiseClone();
        public static Settings Load(string path)
        {
            if (!File.Exists(path)) return new Settings();
            var value = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8));
            if (value == null) throw new FormatException(Words.T("設定ファイルが空です。", "The settings file is empty."));
            value.Validate(); return value;
        }
        public void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak"); else File.Move(tmp, path);
        }
    }
}
