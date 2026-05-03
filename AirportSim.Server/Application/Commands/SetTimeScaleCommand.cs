using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record SetTimeScaleCommand(double Scale) : IRequest<string>;

public class SetTimeScaleHandler : IRequestHandler<SetTimeScaleCommand, string>
{
    private readonly ISimulationService _sim;
    public SetTimeScaleHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(SetTimeScaleCommand cmd, CancellationToken ct)
    {
        double applied = _sim.Clock.SetTimeScale(cmd.Scale);
        return Task.FromResult($"⏩ Speed set to {applied}x");
    }
}