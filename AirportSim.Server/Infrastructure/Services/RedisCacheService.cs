using System.Text.Json;
using AirportSim.Server.Domain.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AirportSim.Server.Infrastructure.Services;

/// <summary>
/// Redis implementation of ICacheService using IDistributedCache
/// (Microsoft's abstraction over StackExchange.Redis).
///
/// Design rules:
///  - NEVER throw to callers — all exceptions are caught and logged.
///  - Serialization is System.Text.Json — same as SignalR, no extra deps.
///  - TTL defaults to 5 seconds for live snapshot data.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache          _cache;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            string json = JsonSerializer.Serialize(value, JsonOpts);
            var options = new DistributedCacheEntryOptions();

            if (ttl.HasValue)
                options.SetAbsoluteExpiration(ttl.Value);

            await _cache.SetStringAsync(key, json, options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key '{Key}' — cache miss will be used.", key);
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            string? json = await _cache.GetStringAsync(key, ct);
            if (json is null) return default;
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key '{Key}' — falling back to source.", key);
            return default;
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis REMOVE failed for key '{Key}'.", key);
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            // SetString with a 1-second TTL as a lightweight connectivity check
            await _cache.SetStringAsync("__ping__", "1",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1)
                }, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}