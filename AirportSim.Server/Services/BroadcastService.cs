// BroadcastService is intentionally kept minimal in this architecture.
// SimulationEngine (BackgroundService) owns the loop and calls the hub
// directly via IHubContext — this is the recommended ASP.NET Core pattern
// for pushing from a background service without coupling to hub lifetime.
//
// This file is preserved as a thin diagnostic helper that reports server
// health into the alert log every 60 real seconds, so operators can confirm
// the engine is alive without attaching a debugger.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AirportSim.Server.Services
{
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
                _logger.LogInformation(
                    "AirportSim engine heartbeat — {Time}", DateTimeOffset.UtcNow);
            }
        }
    }
}