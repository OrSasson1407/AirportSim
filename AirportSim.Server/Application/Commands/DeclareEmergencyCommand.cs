using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record DeclareEmergencyCommand : IRequest<string>;

public class DeclareEmergencyHandler : IRequestHandler<DeclareEmergencyCommand, string>
{
    private readonly ISimulationService _sim;
    public DeclareEmergencyHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(DeclareEmergencyCommand cmd, CancellationToken ct)
    {
        _sim.InjectEmergency();
        return Task.FromResult("🚨 MAYDAY declared — emergency aircraft inbound");
    }
}