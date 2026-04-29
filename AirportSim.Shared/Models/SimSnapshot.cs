using System;
using System.Collections.Generic;

namespace AirportSim.Shared.Models
{
    public class RunwaySnapshot
    {
        public RunwayId     Id     { get; set; }
        public string       Name   { get; set; } = string.Empty;   // e.g. "28L ARR"
        public RunwayStatus Status { get; set; }
        public string?      OccupiedBy { get; set; }               // FlightId or null
    }

    public class SimSnapshot
    {
        // Time + control
        public DateTime SimulatedTime { get; set; }
        public double   TimeScale     { get; set; }
        public bool     IsPaused      { get; set; }

        // Runways — replaces single RunwayStatus
        public List<RunwaySnapshot> Runways { get; set; } = new();

        // Keep legacy property so the client status bar compiles unchanged
        // (true if either runway is occupied)
        public RunwayStatus RunwayStatus =>
            Runways.Exists(r => r.Status == RunwayStatus.Occupied)
                ? RunwayStatus.Occupied
                : RunwayStatus.Free;

        // Aircraft
        public List<AircraftState> ActiveAircraft { get; set; } = new();

        // Queue preview (next 5 scheduled flights)
        public List<FlightEvent> QueuedFlights { get; set; } = new();

        // Weather
        public WeatherCondition Weather { get; set; } = WeatherCondition.Clear;

        // Alert log (last 5 events, newest first)
        public List<string> RecentAlerts { get; set; } = new();

        // Daily counters
        public int TotalArrivalsToday   { get; set; }
        public int TotalDeparturesToay  { get; set; }
        public int GoAroundsToday       { get; set; }
    }
}