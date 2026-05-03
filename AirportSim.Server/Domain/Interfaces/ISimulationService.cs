using AirportSim.Server.Infrastructure.Simulation;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Domain.Interfaces;

/// <summary>
/// The contract the Application layer exposes for controlling the simulation.
/// Presentation (Hub, controllers) talks ONLY through this interface.
/// Nothing here references SignalR, EF Core, or any other infrastructure concern.
/// </summary>
public interface ISimulationService
{
    SimClock Clock { get; }

    // ── Control ────────────────────────────────────────────────────────────────
    void ToggleOpsMode(RunwayOpsMode mode);
    void InjectEmergency();
    void GrantClearance(string flightId, string clearanceType);
    void AssignSpeed(string flightId, int speedKts);
    void AssignAltitude(string flightId, int altitudeFt);
    void SetRvr(int rvrMeters);
    WeatherCondition CycleWeather();
    void LoadLayout(string layoutId);

    // ── Query ──────────────────────────────────────────────────────────────────
    List<SimSnapshot> GetReplayBuffer();
    SimSnapshot? GetLatestSnapshot();
}