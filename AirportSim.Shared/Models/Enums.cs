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
        
        // Arrival Phases
        Approaching,
        OnFinal,
        Landing,
        Rollout,
        
        // Departure Phases
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
}