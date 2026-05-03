using AirportSim.Server.Domain.Entities;
using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

/// <summary>
/// Command: persist a completed flight to the database.
/// Fired by SimulationEngine when a flight reaches a terminal phase
/// (Parked after arrival, Departed, or Diverted).
/// 
/// Fire-and-forget from the engine: Send() is not awaited in the hot tick path.
/// The handler is async and runs on the thread pool without blocking simulation.
/// </summary>
public record LogCompletedFlightCommand(
    string   FlightId,
    string   AircraftType,
    string   FlightType,
    string   Origin,
    string   Destination,
    string   AssignedGate,
    string   Outcome,
    int      GoAroundCount,
    int      DelayMinutes,
    double   FinalFuelPct,
    DateTime SimulatedTime
) : IRequest;

public class LogCompletedFlightHandler : IRequestHandler<LogCompletedFlightCommand>
{
    private readonly IFlightLogRepository _repo;
    private readonly ILogger<LogCompletedFlightHandler> _logger;

    public LogCompletedFlightHandler(
        IFlightLogRepository repo,
        ILogger<LogCompletedFlightHandler> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    public async Task Handle(LogCompletedFlightCommand cmd, CancellationToken ct)
    {
        var entry = new FlightLogEntry(
            cmd.FlightId,
            cmd.AircraftType,
            cmd.FlightType,
            cmd.Origin,
            cmd.Destination,
            cmd.AssignedGate,
            cmd.Outcome,
            cmd.GoAroundCount,
            cmd.DelayMinutes,
            cmd.FinalFuelPct,
            cmd.SimulatedTime);

        try
        {
            await _repo.AddAsync(entry, ct);
            _logger.LogDebug("Flight logged: {FlightId} → {Outcome}", cmd.FlightId, cmd.Outcome);
        }
        catch (Exception ex)
        {
            // Never crash the simulation loop over a DB write failure.
            // Log it and move on — the in-memory replay buffer still works.
            _logger.LogError(ex,
                "Failed to persist flight log for {FlightId}. Simulation continues.", cmd.FlightId);
        }
    }
}