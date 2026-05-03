using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record StepSpeedUpCommand : IRequest<string>;

public class StepSpeedUpHandler : IRequestHandler<StepSpeedUpCommand, string>
{
    private readonly ISimulationService _sim;
    public StepSpeedUpHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(StepSpeedUpCommand cmd, CancellationToken ct)
    {
        double applied = _sim.Clock.StepUp();
        return Task.FromResult($"⏩ Speed → {applied}x");
    }
}