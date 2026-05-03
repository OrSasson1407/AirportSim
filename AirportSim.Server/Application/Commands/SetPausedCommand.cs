using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record SetPausedCommand(bool IsPaused) : IRequest<string>;

public class SetPausedHandler : IRequestHandler<SetPausedCommand, string>
{
    private readonly ISimulationService _sim;
    public SetPausedHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(SetPausedCommand cmd, CancellationToken ct)
    {
        _sim.Clock.IsPaused = cmd.IsPaused;
        return Task.FromResult(cmd.IsPaused ? "⏸ Simulation paused" : "▶ Simulation resumed");
    }
}