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
        Pushback, // NEW: Tug pushback phase
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
        Departed,
        
        // Diversion Phase
        Diverted
    }

    public enum RunwayStatus
    {
        Free,
        Occupied
    }

    public enum RunwayId
    {
        Runway28L,
        Runway28R 
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
        GoAround,
        Diverting
    }
}