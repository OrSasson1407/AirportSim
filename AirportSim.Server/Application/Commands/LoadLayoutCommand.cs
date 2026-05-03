using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Commands;

public record LoadLayoutCommand(string LayoutId) : IRequest<string>;

public class LoadLayoutHandler : IRequestHandler<LoadLayoutCommand, string>
{
    private readonly ISimulationService _sim;
    public LoadLayoutHandler(ISimulationService sim) => _sim = sim;

    public Task<string> Handle(LoadLayoutCommand cmd, CancellationToken ct)
    {
        var valid = new[] { "tlv", "lhr", "jfk" };
        if (!valid.Contains(cmd.LayoutId.ToLower()))
            return Task.FromResult($"⚠ Unknown layout '{cmd.LayoutId}'. Valid: tlv, lhr, jfk.");

        _sim.LoadLayout(cmd.LayoutId);
        return Task.FromResult($"🌍 Airport layout changed to {cmd.LayoutId.ToUpper()}");
    }
}