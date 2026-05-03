namespace AirportSim.Server.Domain.Interfaces;

/// <summary>
/// Domain abstraction for distributed caching.
/// The Application layer calls this — the Infrastructure layer (Redis) implements it.
/// All operations silently no-op if the cache is unavailable, so the simulation
/// never crashes due to a Redis outage.
/// </summary>
public interface ICacheService
{
    /// <summary>Store a value. Pass null TTL for no expiry.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>Retrieve a value. Returns default(T) on miss or error.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>Remove a key.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Returns true if the cache is reachable.</summary>
    Task<bool> PingAsync(CancellationToken ct = default);
}