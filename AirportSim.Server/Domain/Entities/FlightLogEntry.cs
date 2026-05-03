namespace AirportSim.Server.Domain.Entities;

/// <summary>
/// Immutable record of a completed simulated flight.
/// Written once when a flight lands, departs, or diverts.
/// This is a domain entity — it has no EF or framework imports.
/// </summary>
public class FlightLogEntry
{
    public long     Id            { get; private set; }   // PK — set by DB
    public string   FlightId      { get; private set; }
    public string   AircraftType  { get; private set; }
    public string   FlightType    { get; private set; }   // "Arrival" | "Departure"
    public string   Origin        { get; private set; }
    public string   Destination   { get; private set; }
    public string   AssignedGate  { get; private set; }
    public string   Outcome       { get; private set; }   // "Landed" | "Departed" | "Diverted" | "GoAround"
    public int      GoAroundCount { get; private set; }
    public int      DelayMinutes  { get; private set; }
    public double   FinalFuelPct  { get; private set; }
    public DateTime SimulatedTime { get; private set; }   // sim clock time at completion
    public DateTime WallClockTime { get; private set; }   // real UTC time written

    // EF Core requires a parameterless constructor for materialization
    private FlightLogEntry() 
    {
        FlightId     = string.Empty;
        AircraftType = string.Empty;
        FlightType   = string.Empty;
        Origin       = string.Empty;
        Destination  = string.Empty;
        AssignedGate = string.Empty;
        Outcome      = string.Empty;
    }

    public FlightLogEntry(
        string   flightId,
        string   aircraftType,
        string   flightType,
        string   origin,
        string   destination,
        string   assignedGate,
        string   outcome,
        int      goAroundCount,
        int      delayMinutes,
        double   finalFuelPct,
        DateTime simulatedTime)
    {
        FlightId      = flightId;
        AircraftType  = aircraftType;
        FlightType    = flightType;
        Origin        = origin;
        Destination   = destination;
        AssignedGate  = assignedGate;
        Outcome       = outcome;
        GoAroundCount = goAroundCount;
        DelayMinutes  = delayMinutes;
        FinalFuelPct  = finalFuelPct;
        SimulatedTime = simulatedTime;
        WallClockTime = DateTime.UtcNow;
    }
}