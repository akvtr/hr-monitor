using System;

namespace HeartRateMonitor
{
    public sealed class Measurement
    {
        public int Bpm { get; set; }
        public bool? Contact { get; set; }
        public int? Energy { get; set; }
        public double[] RrMilliseconds { get; set; }

        public static Measurement Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2) throw new FormatException("心拍通知が短すぎます。");
            int flags = bytes[0], index = 1;
            Func<int> word = () => {
                if (index + 2 > bytes.Length) throw new FormatException("心拍通知が途中で切れています。");
                int value = bytes[index] | (bytes[index + 1] << 8); index += 2; return value;
            };
            var result = new Measurement {
                Bpm = (flags & 1) != 0 ? word() : bytes[index++],
                Contact = (flags & 4) != 0 ? (bool?)((flags & 2) != 0) : null,
                RrMilliseconds = new double[0]
            };
            if ((flags & 8) != 0) result.Energy = word();
            if ((flags & 16) != 0) {
                if (index == bytes.Length || (bytes.Length - index) % 2 != 0) throw new FormatException("RR間隔の形式が不正です。");
                var rr = new System.Collections.Generic.List<double>();
                while (index < bytes.Length) rr.Add(word() * 1000.0 / 1024);
                result.RrMilliseconds = rr.ToArray();
            }
            if (index != bytes.Length) throw new FormatException("心拍通知に余分なデータがあります。");
            return result;
        }
    }

    public sealed class Reading
    {
        public long Id { get; set; }
        public long UnixMs { get; set; }
        public string Session { get; set; }
        public string Device { get; set; }
        public int Bpm { get; set; }
        public bool? Contact { get; set; }
        public string Rr { get; set; }
        public int? Energy { get; set; }
        public string Time => DateTimeOffset.FromUnixTimeMilliseconds(UnixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        public string ContactText => Contact.HasValue ? (Contact.Value ? "接触あり" : "接触なし") : "未対応";
    }
}
