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
        GoAround,       // NEW: aborted landing, climbing back out
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

    // NEW: current weather conditions at the airport
    public enum WeatherCondition
    {
        Clear,
        Cloudy,
        Rain,
        Fog,            // reduces visibility, forces go-arounds more often
        Storm           // grounds departures, increases go-around chance
    }

    // NEW: special status flags for an aircraft
    public enum AircraftStatus
    {
        Normal,
        Emergency,      // squawking 7700 — flashes red on client
        GoAround        // actively climbing away from a missed approach
    }
}