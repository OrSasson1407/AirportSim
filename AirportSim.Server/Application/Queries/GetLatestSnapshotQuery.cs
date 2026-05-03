using AirportSim.Server.Domain.Interfaces;
using AirportSim.Shared.Models;
using MediatR;

namespace AirportSim.Server.Application.Queries;

/// <summary>
/// Query: fetch the most recent simulation snapshot without mutating any state.
/// In Step 4 (Redis), the handler can return a cached value instead of
/// calling into the engine — the Hub and any REST controller won't change at all.
/// </summary>
public record GetLatestSnapshotQuery : IRequest<SimSnapshot?>;

public class GetLatestSnapshotHandler : IRequestHandler<GetLatestSnapshotQuery, SimSnapshot?>
{
    private readonly ISimulationService _sim;
    public GetLatestSnapshotHandler(ISimulationService sim) => _sim = sim;

    public Task<SimSnapshot?> Handle(GetLatestSnapshotQuery query, CancellationToken ct)
        => Task.FromResult(_sim.GetLatestSnapshot());
}