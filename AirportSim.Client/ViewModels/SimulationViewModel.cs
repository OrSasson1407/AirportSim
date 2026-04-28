using System;
using AirportSim.Shared.Models;
using AirportSim.Client.Connection;

namespace AirportSim.Client.ViewModels
{
    public class SimulationViewModel
    {
        public readonly SimulationConnection Connection;

        public SimSnapshot? PreviousSnapshot { get; private set; }
        public SimSnapshot? TargetSnapshot { get; private set; }
        
        // Tracks exactly when the network packet arrived
        public DateTime LastSnapshotTime { get; private set; }

        public SimulationViewModel()
        {
            Connection = new SimulationConnection();
            Connection.OnSnapshotReceived += HandleNewSnapshot;
        }

        public async void Start()
        {
            await Connection.ConnectAsync();
        }

        private void HandleNewSnapshot(SimSnapshot newSnapshot)
        {
            // The old target becomes our new starting point
            PreviousSnapshot = TargetSnapshot ?? newSnapshot; 
            
            // The fresh snapshot is where we want to interpolate towards
            TargetSnapshot = newSnapshot;
            LastSnapshotTime = DateTime.UtcNow;
        }
        
        // The custom Avalonia Canvas will call this 60 times a second
        public double GetInterpolationT()
        {
            if (TargetSnapshot == null || PreviousSnapshot == null) return 0;
            
            double elapsedMs = (DateTime.UtcNow - LastSnapshotTime).TotalMilliseconds;
            
            // Server broadcasts every 200ms, so we divide elapsed time by 200
            double t = elapsedMs / 200.0;
            
            return Math.Clamp(t, 0.0, 1.0);
        }
    }
}