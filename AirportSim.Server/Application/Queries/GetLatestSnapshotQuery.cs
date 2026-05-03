using AirportSim.Server.Domain.Interfaces;
using AirportSim.Shared.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AirportSim.Server.Application.Queries;

/// <summary>
/// Cache key for the latest snapshot. Kept as a constant so it's
/// consistent between the writer (SimulationEngine) and the reader (this handler).
/// </summary>
public static class CacheKeys
{
    public const string LatestSnapshot = "sim:snapshot:latest";
    public const string LayoutPrefix   = "sim:layout:";
}

/// <summary>
/// Query: returns the most recent simulation snapshot.
///
/// Read path (cache-first):
///   1. Try Redis  → return immediately if hit (fast path, ~0.2ms)
///   2. Miss       → ask SimulationEngine in memory (~0ms, but adds lock contention)
///   3. Back-fill  → write result to Redis for next caller
///
/// With 200 connected clients polling at 5Hz, this drops engine reads from
/// 1,000/s to ~1/s (one writer, many readers from cache).
/// </summary>
public record GetLatestSnapshotQuery : IRequest<SimSnapshot?>;

public class GetLatestSnapshotHandler : IRequestHandler<GetLatestSnapshotQuery, SimSnapshot?>
{
    private readonly ISimulationService             _sim;
    private readonly ICacheService                  _cache;
    private readonly ILogger<GetLatestSnapshotHandler> _logger;

    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(2);

    public GetLatestSnapshotHandler(
        ISimulationService sim,
        ICacheService cache,
        ILogger<GetLatestSnapshotHandler> logger)
    {
        _sim    = sim;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<SimSnapshot?> Handle(GetLatestSnapshotQuery query, CancellationToken ct)
    {
        // 1. Cache-first
        var cached = await _cache.GetAsync<SimSnapshot>(CacheKeys.LatestSnapshot, ct);
        if (cached is not null)
            return cached;

        // 2. Cache miss — read from engine
        var snapshot = _sim.GetLatestSnapshot();
        if (snapshot is null) return null;

        // 3. Back-fill cache
        await _cache.SetAsync(CacheKeys.LatestSnapshot, snapshot, SnapshotTtl, ct);

        return snapshot;
    }
}