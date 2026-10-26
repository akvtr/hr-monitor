using System;
using System.Net;
using System.Net.Sockets;

namespace HeartRateMonitor
{
    public sealed class OscSender : IDisposable
    {
        private UdpClient client;
        private string endpointKey;
        private bool disposed;
        public static byte[] Packet(string address, char type, int bits)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (address.Length == 0 || address[0] != '/') throw new ArgumentException("OSC addresses must start with '/'.", nameof(address));
            foreach (char value in address)
                if (value < 33 || value > 126) throw new ArgumentException("OSC addresses must contain printable ASCII without spaces.", nameof(address));
            if (type != 'i' && type != 'f' && type != 'c' && type != 'r' && type != 'm')
                throw new ArgumentOutOfRangeException(nameof(type), "This encoder supports a single fixed-width 32-bit OSC argument.");

            // Include the address terminator and padding, one four-byte type tag, and one argument.
            int addressSize = checked((address.Length + 4) / 4 * 4);
            var packet = new byte[checked(addressSize + 8)];
            for (int i = 0; i < address.Length; i++) packet[i] = (byte)address[i];
            packet[addressSize] = (byte)',';
            packet[addressSize + 1] = (byte)type;
            int payload = addressSize + 4;
            packet[payload] = (byte)(bits >> 24);
            packet[payload + 1] = (byte)(bits >> 16);
            packet[payload + 2] = (byte)(bits >> 8);
            packet[payload + 3] = (byte)bits;
            return packet;
        }
        public void Send(Settings settings, int bpm)
        {
            if (disposed) throw new ObjectDisposedException(nameof(OscSender));
            if (!settings.OscEnabled) return;
            Send(settings, new OscValues(settings, bpm));
        }
        internal void Send(Settings settings, OscValues values)
        {
            if (disposed) throw new ObjectDisposedException(nameof(OscSender));
            string key = settings.Host + ":" + settings.Port;
            if (client == null || key != endpointKey) {
                client?.Dispose(); client = null;
                var ip = IPAddress.Parse(settings.Host);
                var next = new UdpClient(ip.AddressFamily);
                try { next.Connect(new IPEndPoint(ip, settings.Port)); client = next; endpointKey = key; }
                catch { next.Dispose(); throw; }
            }
            var integer = Packet(values.IntAddress, 'i', values.Integer);
            var floating = Packet(values.FloatAddress, 'f', BitConverter.ToInt32(BitConverter.GetBytes(values.Percent), 0));
            client.Send(integer, integer.Length); client.Send(floating, floating.Length);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client?.Dispose();
            client = null;
            endpointKey = null;
        }
    }
}
