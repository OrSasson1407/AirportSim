using AirportSim.Server.Domain.Entities;

namespace AirportSim.Server.Domain.Interfaces;

/// <summary>
/// Domain interface for flight log persistence.
/// The Application layer uses this — it never imports EF Core or Npgsql directly.
/// </summary>
public interface IFlightLogRepository
{
    Task AddAsync(FlightLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<FlightLogEntry>> GetRecentAsync(int count = 50, CancellationToken ct = default);
    Task<IReadOnlyList<FlightLogEntry>> GetByFlightIdAsync(string flightId, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
}