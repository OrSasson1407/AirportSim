namespace AirportSim.Shared.Models
{
    // Lightweight struct — keeps the shared library UI-agnostic
    public struct SimPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public SimPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        // NEW: convenience for interpolation on the server side
        public static SimPoint Lerp(SimPoint a, SimPoint b, double t) =>
            new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public class AircraftState
    {
        // Core identity
        public string FlightId   { get; set; } = string.Empty;  // e.g. "EL-AL 742"
        public AircraftType Type { get; set; }                   // Small / Medium / Large
        public FlightType FlightType { get; set; }               // Arrival / Departure

        // Phase / motion
        public AircraftPhase Phase         { get; set; }
        public double        PhaseProgress { get; set; }         // 0.0 → 1.0
        public SimPoint      Position      { get; set; }         // world-space x, y
        public double        Heading       { get; set; }         // degrees (0 = right, 90 = down)

        // NEW: special status (normal / emergency / go-around)
        public AircraftStatus Status { get; set; } = AircraftStatus.Normal;

        // NEW: altitude in feet (pseudo, derived from Y on server, used for labels)
        public int AltitudeFt { get; set; }

        // NEW: airspeed in knots (approximate, per phase and aircraft type)
        public int SpeedKts { get; set; }

        // NEW: how many go-arounds this aircraft has already attempted this approach
        public int GoAroundCount { get; set; }
    }
}