using System;

namespace AirportSim.Shared.Models
{
    public class FlightEvent
    {
        public string FlightId { get; set; } = string.Empty;
        public AircraftType Type { get; set; }
        public FlightType FlightType { get; set; }
        public DateTime ScheduledTime { get; set; } 
    }
}