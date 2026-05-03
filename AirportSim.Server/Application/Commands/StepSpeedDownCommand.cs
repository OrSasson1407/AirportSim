using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record StepSpeedDownCommand : IRequest<string>;

public class StepSpeedDownHandler : IRequestHandler<StepSpeedDownCommand, string>
{
    private readonly ISimulationService _sim;
    public StepSpeedDownHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(StepSpeedDownCommand cmd, CancellationToken ct)
    {
        double applied = _sim.Clock.StepDown();
        return Task.FromResult($"⏪ Speed → {applied}x");
    }
}