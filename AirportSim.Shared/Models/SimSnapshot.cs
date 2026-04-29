using System;
using System.Collections.Generic;

namespace AirportSim.Shared.Models
{
    public class SimSnapshot
    {
        // Time + control
        public DateTime SimulatedTime { get; set; }
        public double   TimeScale     { get; set; }
        public bool     IsPaused      { get; set; }   // NEW: client reflects actual pause state

        // Runway
        public RunwayStatus RunwayStatus { get; set; }

        // Aircraft
        public List<AircraftState> ActiveAircraft { get; set; } = new();

        // Queue preview (next 5 scheduled flights)
        public List<FlightEvent> QueuedFlights { get; set; } = new();

        // NEW: current weather at the airport
        public WeatherCondition Weather { get; set; } = WeatherCondition.Clear;

        // NEW: scrolling alert log (last 5 events, newest first)
        //      e.g. "EL-AL 742 declared emergency", "LH 301 executing go-around"
        public List<string> RecentAlerts { get; set; } = new();

        // NEW: total counts for the status bar
        public int TotalArrivalsToday   { get; set; }
        public int TotalDeparturesToay  { get; set; }
        public int GoAroundsToday       { get; set; }
    }
}