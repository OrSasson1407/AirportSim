namespace AirportSim.Shared.Models
{
    // A lightweight struct to keep the shared library UI-agnostic
    public struct SimPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public SimPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public class AircraftState
    {
        public string FlightId { get; set; } = string.Empty;     // e.g. "EL-AL 742" [cite: 83]
        public AircraftType Type { get; set; }                   // Small / Medium / Large [cite: 84]
        public FlightType FlightType { get; set; }               // Arrival / Departure [cite: 85]
        public AircraftPhase Phase { get; set; }                 // e.g. Taxiing, Approaching [cite: 86]
        public double PhaseProgress { get; set; }                // 0.0 -> 1.0 [cite: 87]
        public SimPoint Position { get; set; }                   // world-space x, y [cite: 88]
        public double Heading { get; set; }                      // degrees [cite: 89]
    }
}