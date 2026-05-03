using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record SetRvrCommand(int RvrMeters) : IRequest;

public class SetRvrHandler : IRequestHandler<SetRvrCommand>
{
    private readonly ISimulationService _sim;
    public SetRvrHandler(ISimulationService sim) => _sim = sim;

    public Task Handle(SetRvrCommand cmd, CancellationToken ct)
    {
        _sim.SetRvr(cmd.RvrMeters);
        return Task.CompletedTask;
    }
}