using System;

namespace AirportSim.Server.Simulation
{
    public class SimClock
    {
        public DateTime SimulatedNow { get; private set; }
        public double   TimeScale    { get; private set; } = 60.0;
        public bool     IsPaused     { get; set; }         = false;

        public static readonly double[] SpeedPresets = { 1.0, 10.0, 30.0, 60.0, 120.0, 300.0 };

        public SimClock(DateTime startTime)
        {
            SimulatedNow = startTime;
        }

        public void Tick(int realDeltaMs)
        {
            if (IsPaused) return;
            SimulatedNow = SimulatedNow.AddMilliseconds(realDeltaMs * TimeScale);
        }

        public double SetTimeScale(double requested)
        {
            TimeScale = Math.Clamp(requested, SpeedPresets[0], SpeedPresets[^1]);
            return TimeScale;
        }

        public double StepUp()
        {
            foreach (var p in SpeedPresets)
                if (p > TimeScale) { TimeScale = p; return p; }
            return TimeScale;
        }

        public double StepDown()
        {
            for (int i = SpeedPresets.Length - 1; i >= 0; i--)
                if (SpeedPresets[i] < TimeScale) { TimeScale = SpeedPresets[i]; return SpeedPresets[i]; }
            return TimeScale;
        }

        public bool IsNight  => SimulatedNow.Hour >= 19 || SimulatedNow.Hour < 5;
        public bool IsDawn   => SimulatedNow.Hour == 5;
        public bool IsDusk   => SimulatedNow.Hour == 18;
        public bool IsDay    => !IsNight && !IsDawn && !IsDusk;
    }
}