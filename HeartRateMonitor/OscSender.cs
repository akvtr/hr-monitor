using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HeartRateMonitor
{
    public sealed class OscSender : IDisposable
    {
        private UdpClient client;
        private string endpointKey;
        public static byte[] Packet(string address, char type, int bits)
        {
            using (var stream = new MemoryStream()) {
                Action<string> str = s => {
                    byte[] data = Encoding.ASCII.GetBytes(s); stream.Write(data, 0, data.Length); stream.WriteByte(0);
                    while (stream.Length % 4 != 0) stream.WriteByte(0);
                };
                str(address); str("," + type);
                stream.WriteByte((byte)(bits >> 24)); stream.WriteByte((byte)(bits >> 16));
                stream.WriteByte((byte)(bits >> 8)); stream.WriteByte((byte)bits);
                return stream.ToArray();
            }
        }
        public void Send(Settings settings, int bpm)
        {
            if (!settings.OscEnabled) return;
            string key = settings.Host + ":" + settings.Port;
            if (client == null || key != endpointKey) {
                client?.Dispose(); client = null;
                var ip = IPAddress.Parse(settings.Host);
                var next = new UdpClient(ip.AddressFamily);
                try { next.Connect(new IPEndPoint(ip, settings.Port)); client = next; endpointKey = key; }
                catch { next.Dispose(); throw; }
            }
            var integer = Packet(Settings.Address(settings.IntParameter), 'i', Math.Max(0, Math.Min(255, bpm)));
            var value = settings.Percent(bpm);
            var floating = Packet(Settings.Address(settings.FloatParameter), 'f', BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
            client.Send(integer, integer.Length); client.Send(floating, floating.Length);
        }
        public void Dispose() { client?.Dispose(); }
    }
}
