using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record CycleWeatherCommand : IRequest<string>;

public class CycleWeatherHandler : IRequestHandler<CycleWeatherCommand, string>
{
    private readonly ISimulationService _sim;
    public CycleWeatherHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(CycleWeatherCommand cmd, CancellationToken ct)
    {
        var next = _sim.CycleWeather();
        return Task.FromResult($"🌤 Weather changed to {next}");
    }
}