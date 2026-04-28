using System;

namespace AirportSim.Server.Simulation
{
    public class SimClock
    {
        public DateTime SimulatedNow { get; private set; }
        public double TimeScale { get; set; } = 60.0; 
        public bool IsPaused { get; set; } = false;   

        public SimClock(DateTime startTime)
        {
            SimulatedNow = startTime;
        }

        public void Tick(int realDeltaMs)
        {
            if (IsPaused) return;
            
            double simDeltaMs = realDeltaMs * TimeScale;
            SimulatedNow = SimulatedNow.AddMilliseconds(simDeltaMs);
        }
        
        public void SetTimeScale(double newScale)
        {
            TimeScale = Math.Max(1.0, newScale); 
        }
    }
}