using AirportSim.Server.Domain.Interfaces;
using AirportSim.Shared.Models;
using MediatR;

namespace AirportSim.Server.Application.Queries;

/// <summary>
/// Query: returns the rolling 600-frame replay buffer.
/// Read-only — safe to serve from a cache or a read replica in future steps.
/// </summary>
public record GetReplayBufferQuery : IRequest<List<SimSnapshot>>;

public class GetReplayBufferHandler : IRequestHandler<GetReplayBufferQuery, List<SimSnapshot>>
{
    private readonly ISimulationService _sim;
    public GetReplayBufferHandler(ISimulationService sim) => _sim = sim;

    public Task<List<SimSnapshot>> Handle(GetReplayBufferQuery query, CancellationToken ct)
        => Task.FromResult(_sim.GetReplayBuffer());
}