using System;
using System.Collections.Generic;

namespace AirportSim.Shared.Models
{
    public enum GroundVehicleType { BaggageCart, FuelTruck, FireEngine, Catering }

    public class GroundVehicleState
    {
        public string Id { get; set; } = string.Empty;
        public GroundVehicleType Type { get; set; }
        public SimPoint Position { get; set; }
        public double Heading { get; set; }
    }

    public class RunwaySnapshot
    {
        public RunwayId     Id     { get; set; }
        public string       Name   { get; set; } = string.Empty;   
        public RunwayStatus Status { get; set; }
        public string?      OccupiedBy { get; set; }               
    }

    // NEW: Gate Departure Board Entry
    public class FidsEntry
    {
        public string FlightId { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string Gate { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class SimSnapshot
    {
        public DateTime SimulatedTime { get; set; }
        public double   TimeScale     { get; set; }
        public bool     IsPaused      { get; set; }

        public List<RunwaySnapshot> Runways { get; set; } = new();

        public RunwayStatus RunwayStatus =>
            Runways.Exists(r => r.Status == RunwayStatus.Occupied)
                ? RunwayStatus.Occupied
                : RunwayStatus.Free;

        public List<AircraftState> ActiveAircraft { get; set; } = new();
        public List<GroundVehicleState> GroundVehicles { get; set; } = new();
        public List<FlightEvent> QueuedFlights { get; set; } = new();

        // NEW: Gate Status Board
        public List<FidsEntry> DepartureBoard { get; set; } = new();

        public WeatherCondition Weather { get; set; } = WeatherCondition.Clear;
        public WeatherCondition PreviousWeather { get; set; } = WeatherCondition.Clear;
        public double WeatherTransitionProgress { get; set; } = 1.0; 

        public bool IsWindShearActive { get; set; }
        public int RvrMeters { get; set; } = 10000;

        // NEW: Radar Blob location
        public SimPoint? StormCenter { get; set; }

        public string CurrentAtis { get; set; } = string.Empty;

        public List<string> RecentAlerts { get; set; } = new();

        // Daily counters
        public int TotalArrivalsToday   { get; set; }
        public int TotalDeparturesToay  { get; set; }
        public int GoAroundsToday       { get; set; }
        
        // Performance Rating Metrics
        public int DiversionsToday      { get; set; }
        public int ConflictCountToday   { get; set; }
        public int TotalDelayMinutes    { get; set; }
        public string CurrentScoreGrade { get; set; } = "A+";
    }
}