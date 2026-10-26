using System;
using System.Diagnostics;
using System.Threading;

namespace HeartRateMonitor
{
    // Silence is an elapsed interval; wall-clock changes must not affect reconnection or OSC.
    internal sealed class ReceptionTimer
    {
        private readonly Func<long> timestamp;
        private long lastReception;

        internal ReceptionTimer(Func<long> timestamp = null)
        {
            this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
            Reset();
        }

        internal void Reset() => Interlocked.Exchange(ref lastReception, timestamp());
        internal double ElapsedSeconds => (timestamp() - Interlocked.Read(ref lastReception)) / (double)Stopwatch.Frequency;
    }
}
