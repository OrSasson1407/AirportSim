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

        // Runways
        public List<RunwaySnapshot> Runways { get; set; } = new();

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

        // Weather transition tracking
        public WeatherCondition PreviousWeather { get; set; } = WeatherCondition.Clear;
        public double WeatherTransitionProgress { get; set; } = 1.0; // 0.0 to 1.0

        // NEW: Microburst / Wind shear state
        public bool IsWindShearActive { get; set; }

        // Alert log (last 5 events, newest first)
        public List<string> RecentAlerts { get; set; } = new();

        // Daily counters
        public int TotalArrivalsToday   { get; set; }
        public int TotalDeparturesToay  { get; set; }
        public int GoAroundsToday       { get; set; }
    }
}