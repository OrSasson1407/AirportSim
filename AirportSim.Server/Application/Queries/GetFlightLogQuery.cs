using AirportSim.Server.Domain.Entities;
using AirportSim.Server.Domain.Interfaces;
using MediatR;

namespace AirportSim.Server.Application.Queries;

/// <summary>
/// Query: fetch recent completed flights from the persistent log.
/// Completely separate read path — hits the DB read replica in future,
/// never touches the simulation engine or its in-memory state.
/// </summary>
public record GetFlightLogQuery(int Count = 50) : IRequest<IReadOnlyList<FlightLogEntry>>;

public class GetFlightLogHandler : IRequestHandler<GetFlightLogQuery, IReadOnlyList<FlightLogEntry>>
{
    private readonly IFlightLogRepository _repo;

    public GetFlightLogHandler(IFlightLogRepository repo) => _repo = repo;

    public Task<IReadOnlyList<FlightLogEntry>> Handle(
        GetFlightLogQuery query, CancellationToken ct)
        => _repo.GetRecentAsync(query.Count, ct);
}