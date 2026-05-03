using AirportSim.Server.Domain.Entities;
using AirportSim.Server.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AirportSim.Server.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IFlightLogRepository.
/// Uses a DbContext factory (not a scoped DbContext) because this is
/// called from a singleton BackgroundService — the factory creates
/// a fresh short-lived context per operation, which is the correct
/// pattern for background services with EF Core.
/// </summary>
public class FlightLogRepository : IFlightLogRepository
{
    private readonly IDbContextFactory<SimulationDbContext> _factory;

    public FlightLogRepository(IDbContextFactory<SimulationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task AddAsync(FlightLogEntry entry, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.FlightLog.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FlightLogEntry>> GetRecentAsync(
        int count = 50, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FlightLog
            .OrderByDescending(e => e.SimulatedTime)
            .Take(count)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FlightLogEntry>> GetByFlightIdAsync(
        string flightId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FlightLog
            .Where(e => e.FlightId == flightId)
            .OrderByDescending(e => e.SimulatedTime)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.FlightLog.CountAsync(ct);
    }
}