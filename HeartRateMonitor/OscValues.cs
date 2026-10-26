using System;

namespace HeartRateMonitor
{
    // The wire payload and transmission status must describe the same converted values.
    internal sealed class OscValues
    {
        internal string IntAddress { get; }
        internal string FloatAddress { get; }
        internal int Integer { get; }
        internal float Percent { get; }

        internal OscValues(Settings settings, int bpm, bool timeout = false)
        {
            IntAddress = Settings.Address(settings.IntParameter);
            FloatAddress = Settings.Address(settings.FloatParameter);
            Integer = timeout ? settings.OscTimeoutInt : Math.Max(0, Math.Min(255, bpm));
            Percent = timeout ? settings.OscTimeoutFloat : settings.Percent(bpm);
        }
    }
}
