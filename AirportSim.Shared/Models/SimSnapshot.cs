using System;
using System.Collections.Generic;

namespace AirportSim.Shared.Models
{
    public class SimSnapshot
    {
        public DateTime SimulatedTime { get; set; }              // [cite: 76]
        public double TimeScale { get; set; }                    // [cite: 77]
        public RunwayStatus RunwayStatus { get; set; }           // (Free / Occupied) [cite: 78]
        
        public List<AircraftState> ActiveAircraft { get; set; } = new(); // [cite: 79]
        public List<FlightEvent> QueuedFlights { get; set; } = new();    // next 5 in buffer [cite: 80]
    }
}