using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record AssignSpeedCommand(string FlightId, int SpeedKts)
    : IRequest<string>;

public class AssignSpeedHandler : IRequestHandler<AssignSpeedCommand, string>
{
    private readonly ISimulationService _sim;
    public AssignSpeedHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(AssignSpeedCommand cmd, CancellationToken ct)
    {
        if (cmd.SpeedKts is < 80 or > 600)
            return Task.FromResult($"⚠ Speed {cmd.SpeedKts}kts is out of valid range (80–600).");

        _sim.AssignSpeed(cmd.FlightId, cmd.SpeedKts);
        return Task.FromResult($"🎤 ATC: {cmd.FlightId} reduce speed to {cmd.SpeedKts} knots.");
    }
}