using System;

namespace HeartRateMonitor
{
    public sealed class OscTransmission
    {
        public DateTime Time = DateTime.Now;
        public string Endpoint, IntAddress, FloatAddress;
        public int Integer;
        public float Percent;
        public bool Test;
        public bool Timeout;
    }
}
