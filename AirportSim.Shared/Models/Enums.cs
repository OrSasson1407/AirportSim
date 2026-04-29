namespace AirportSim.Shared.Models
{
    public enum AircraftType
    {
        Small,
        Medium,
        Large
    }

    public enum FlightType
    {
        Arrival,
        Departure
    }

    public enum AircraftPhase
    {
        // Shared
        Parked,
        Taxiing,

        // Arrival phases
        Approaching,
        OnFinal,
        GoAround,
        Landing,
        Rollout,

        // Departure phases
        Holding,
        Takeoff,
        Climbing,
        Departed
    }

    public enum RunwayStatus
    {
        Free,
        Occupied
    }

    // Which physical runway an aircraft is assigned to
    public enum RunwayId
    {
        Runway28L,   // arrivals runway  (left  / bottom strip)
        Runway28R    // departures runway (right / top strip)
    }

    public enum WeatherCondition
    {
        Clear,
        Cloudy,
        Rain,
        Fog,
        Storm
    }

    public enum AircraftStatus
    {
        Normal,
        Emergency,
        GoAround
    }
}