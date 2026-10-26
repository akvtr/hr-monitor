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
            var result = new Measurement {
                Bpm = (flags & 1) != 0 ? ReadUInt16LittleEndian(bytes, ref index) : bytes[index++],
                Contact = (flags & 4) != 0 ? (bool?)((flags & 2) != 0) : null,
                RrMilliseconds = Array.Empty<double>()
            };
            if ((flags & 8) != 0) result.Energy = ReadUInt16LittleEndian(bytes, ref index);
            if ((flags & 16) != 0) {
                int remaining = bytes.Length - index;
                if (remaining == 0 || remaining % 2 != 0) throw new FormatException("RR間隔の形式が不正です。");
                var rr = new double[remaining / 2];
                for (int i = 0; i < rr.Length; i++) rr[i] = ReadUInt16LittleEndian(bytes, ref index) * 1000.0 / 1024;
                result.RrMilliseconds = rr;
            }
            if (index != bytes.Length) throw new FormatException("心拍通知に余分なデータがあります。");
            return result;
        }

        private static ushort ReadUInt16LittleEndian(byte[] bytes, ref int index)
        {
            if (bytes.Length - index < 2) throw new FormatException("心拍通知が途中で切れています。");
            ushort value = (ushort)(bytes[index] | (bytes[index + 1] << 8));
            index += 2;
            return value;
        }
    }
}
