using AirportSim.Server.Domain.Interfaces;
using AirportSim.Server.Infrastructure.Simulation;
using AirportSim.Shared.Models;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record ToggleOpsModeCommand(RunwayOpsMode Mode) : IRequest<string>;

public class ToggleOpsModeHandler : IRequestHandler<ToggleOpsModeCommand, string>
{
    private readonly ISimulationService _sim;
    public ToggleOpsModeHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(ToggleOpsModeCommand cmd, CancellationToken ct)
    {
        _sim.ToggleOpsMode(cmd.Mode);
        string label = cmd.Mode == RunwayOpsMode.Segregated
            ? "SEGREGATED (28L Arr / 28R Dep)"
            : "MIXED (Both Runways)";
        return Task.FromResult($"🔄 Runway ops mode: {label}");
    }
}