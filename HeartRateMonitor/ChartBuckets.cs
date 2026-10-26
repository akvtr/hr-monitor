using System;

namespace HeartRateMonitor
{
    internal static class ChartBuckets
    {
        internal const int MaximumPoints = 14400;

        internal static long Width(long from, long to, int maximumPoints)
        {
            if (maximumPoints < 1 || maximumPoints > MaximumPoints) throw new ArgumentOutOfRangeException(nameof(maximumPoints));
            long span = checked(to - from);
            // Divide before rounding up so a valid interval near Int64.MaxValue cannot overflow.
            long width = span / maximumPoints + (span % maximumPoints > 0 ? 1 : 0);
            return Math.Max(1000, width);
        }
    }
}
