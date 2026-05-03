using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record AssignAltitudeCommand(string FlightId, int AltitudeFt)
    : IRequest<string>;

public class AssignAltitudeHandler : IRequestHandler<AssignAltitudeCommand, string>
{
    private readonly ISimulationService _sim;
    public AssignAltitudeHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(AssignAltitudeCommand cmd, CancellationToken ct)
    {
        if (cmd.AltitudeFt is < 0 or > 45000)
            return Task.FromResult($"⚠ Altitude {cmd.AltitudeFt}ft is out of valid range (0–45,000).");

        _sim.AssignAltitude(cmd.FlightId, cmd.AltitudeFt);
        return Task.FromResult($"🎤 ATC: {cmd.FlightId} descend and maintain {cmd.AltitudeFt} feet.");
    }
}