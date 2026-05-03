// Thin diagnostic heartbeat — unchanged from original.
// Moved to Infrastructure/Services/ to match the layer structure.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AirportSim.Server.Infrastructure.Services;

public class BroadcastService : BackgroundService
{
    private readonly ILogger<BroadcastService> _logger;

    public BroadcastService(ILogger<BroadcastService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AirportSim server started at {Time}", DateTimeOffset.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            _logger.LogInformation("AirportSim engine heartbeat — {Time}", DateTimeOffset.UtcNow);
        }
    }
}