using System;

namespace AirportSim.Shared.Models
{
    public class FlightEvent
    {
        // Core identity
        public string      FlightId      { get; set; } = string.Empty;
        public AircraftType Type         { get; set; }
        public FlightType  FlightType    { get; set; }
        public DateTime    ScheduledTime { get; set; }

        // NEW: origin / destination airport codes for display in the queue panel
        public string Origin      { get; set; } = string.Empty;  // e.g. "JFK"
        public string Destination { get; set; } = string.Empty;  // e.g. "TLV"

        // NEW: gate number (departures) or stand number (arrivals)
        public string Gate { get; set; } = string.Empty;         // e.g. "B12"

        // NEW: estimated delay in simulated minutes (0 = on time)
        public int DelayMinutes { get; set; }
    }
}