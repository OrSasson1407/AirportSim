using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AirportSim.Server.Infrastructure.Simulation;

/// <summary>
/// The core heartbeat service.
/// Runs the physics tick at 100ms and broadcasts snapshots at 200ms.
/// Isolated from weather and metrics — if those services restart, this keeps ticking.
/// </summary>
public sealed class SimulationTickService : BackgroundService
{
    private readonly SimulationEngine               _engine;
    private readonly ILogger<SimulationTickService> _logger;

    private const int TickIntervalMs      = 100;
    private const int BroadcastIntervalMs = 200;

    public SimulationTickService(SimulationEngine engine, ILogger<SimulationTickService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _engine.Initialize();
        _logger.LogInformation("SimulationTickService started.");

        var stopwatch = Stopwatch.StartNew();
        long lastTick = stopwatch.ElapsedMilliseconds;
        long lastBroadcast = stopwatch.ElapsedMilliseconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                long now = stopwatch.ElapsedMilliseconds;
                int elapsedMs = (int)(now - lastTick);

                if (elapsedMs >= TickIntervalMs)
                {
                    _engine.Tick(elapsedMs);
                    lastTick = now;
                }

                if ((now - lastBroadcast) >= BroadcastIntervalMs)
                {
                    // Fire and forget broadcast so we don't stall the physics loop
                    _ = Task.Run(() => _engine.BroadcastAsync(stoppingToken), stoppingToken);
                    lastBroadcast = now;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Log but never crash the tick loop
                _logger.LogError(ex, "SimulationTickService: unhandled exception in tick loop.");
            }

            // Brief delay to prevent pegging the CPU to 100%
            await Task.Delay(1, stoppingToken);
        }

        _logger.LogInformation("SimulationTickService stopped.");
    }
}