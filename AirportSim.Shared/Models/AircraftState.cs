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

    public class AircraftState
    {
        // Core identity
        public string       FlightId      { get; set; } = string.Empty;
        public AircraftType Type          { get; set; }
        public FlightType   FlightType    { get; set; }

        // Phase / motion
        public AircraftPhase Phase         { get; set; }
        public double        PhaseProgress { get; set; }
        public SimPoint      Position      { get; set; }
        public double        Heading       { get; set; }

        // Status
        public AircraftStatus Status          { get; set; } = AircraftStatus.Normal;
        public string         EmergencyReason { get; set; } = string.Empty;

        // Telemetry
        public int AltitudeFt    { get; set; }
        public int SpeedKts      { get; set; }
        public int GoAroundCount { get; set; }
        
        // NEW: Real-time telemetry for operational depth
        public double CurrentFuelPercent { get; set; } = 100.0;

        // Gate assignment — set when spawned, used by renderer to position
        // the aircraft at the correct stand while parked
        public string AssignedGate { get; set; } = string.Empty;

        // World-space X position of this gate's stand (set by GateManager,
        // read by Aircraft position logic when Phase == Parked / Taxiing)
        public double GateX { get; set; }
        public double GateY { get; set; }
    }
}