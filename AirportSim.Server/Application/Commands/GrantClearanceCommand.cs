using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

/// <summary>
/// Command: ATC clears a specific flight for a phase transition.
/// clearanceType is one of: "pushback", "taxi", "takeoff", "land"
/// Returns the alert string so the Hub can echo it to all clients.
/// </summary>
public record GrantClearanceCommand(string FlightId, string ClearanceType)
    : IRequest<string>;

public class GrantClearanceHandler : IRequestHandler<GrantClearanceCommand, string>
{
    private readonly ISimulationService _sim;

    public GrantClearanceHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(GrantClearanceCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.FlightId))
            return Task.FromResult("⚠ ClearanceCommand: FlightId is required.");

        var allowed = new[] { "pushback", "taxi", "takeoff", "land" };
        if (!allowed.Contains(cmd.ClearanceType.ToLower()))
            return Task.FromResult($"⚠ Unknown clearance type: {cmd.ClearanceType}");

        _sim.GrantClearance(cmd.FlightId, cmd.ClearanceType);
        return Task.FromResult($"🎤 ATC: {cmd.FlightId} cleared to {cmd.ClearanceType.ToLower()}");
    }
}