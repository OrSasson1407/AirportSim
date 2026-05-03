using System;
using System.Collections.Generic;

namespace AirportSim.Shared.Models
{
    public struct SimPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public SimPoint(double x, double y) { X = x; Y = y; }

        public static SimPoint Lerp(SimPoint a, SimPoint b, double t) =>
            new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public enum TurnaroundPhase { None, Deplaning, Cleaning, Fueling, Boarding, Ready }

    public class AircraftState
    {
        public string       FlightId      { get; set; } = string.Empty;
        public AircraftType Type          { get; set; }
        public FlightType   FlightType    { get; set; }
        public string       Origin        { get; set; } = string.Empty; 
        public string       Destination   { get; set; } = string.Empty; 

        // NEW: Track which runway this aircraft is targeting
        public RunwayId     AssignedRunway { get; set; } 

        public AircraftPhase Phase         { get; set; }
        public double        PhaseProgress { get; set; }
        public SimPoint      Position      { get; set; }
        public double        Heading       { get; set; }
        
        public List<SimPoint> RecentTrail  { get; set; } = new();

        public AircraftStatus Status          { get; set; } = AircraftStatus.Normal;
        public string         EmergencyReason { get; set; } = string.Empty;

        public int AltitudeFt    { get; set; }
        public int SpeedKts      { get; set; }
        public int GoAroundCount { get; set; }
        public double CurrentFuelPercent { get; set; } = 100.0;

        public TurnaroundPhase Turnaround { get; set; } = TurnaroundPhase.None;
        public double TurnaroundProgress { get; set; }
        public int DelayMinutes { get; set; }

        public bool ClearedToPushback { get; set; }
        public bool ClearedToTaxi     { get; set; }
        public bool ClearedToTakeoff  { get; set; }
        public bool ClearedToLand     { get; set; }

        public int? AssignedSpeedKts   { get; set; }
        public int? AssignedAltitudeFt { get; set; }

        public string AssignedGate { get; set; } = string.Empty;
        public double GateX { get; set; }
        public double GateY { get; set; }
    }
}