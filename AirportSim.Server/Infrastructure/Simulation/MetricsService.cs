using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AirportSim.Server.Infrastructure.Simulation;

/// <summary>
/// Computes the score grade and resets daily counters at sim midnight.
/// Runs every 5 seconds — score doesn't need sub-second precision.
///
/// Isolated: score calculation never blocks the physics tick.
/// </summary>
public sealed class MetricsService : BackgroundService
{
    private readonly SimulationEngine        _engine;
    private readonly ILogger<MetricsService> _logger;

    private const int ServiceIntervalMs = 5000;
    private int _lastResetDay = -1;

    public MetricsService(SimulationEngine engine, ILogger<MetricsService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricsService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                UpdateMetrics();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "MetricsService: unhandled exception.");
            }

            await Task.Delay(ServiceIntervalMs, stoppingToken);
        }

        _logger.LogInformation("MetricsService stopped.");
    }

    private void UpdateMetrics()
    {
        // ── Recompute score grade ─────────────────────────────────────────────
        int score = Math.Max(0, 100
            - _engine.GoAroundsToday   * 2
            - _engine.DiversionsToday  * 10
            - _engine.ConflictCount    * 5
            - _engine.TotalDelayMinutes / 10);

        _engine.CurrentScoreGrade = score switch
        {
            >= 95 => "A+", >= 90 => "A", >= 85 => "B+",
            >= 80 => "B",  >= 70 => "C", >= 60 => "D",
            _     => "F"
        };

        // ── Daily reset at sim midnight ───────────────────────────────────────
        int simDay = _engine.Clock.SimulatedNow.DayOfYear;
        if (_lastResetDay != -1 && simDay != _lastResetDay)
        {
            _engine.ArrivalsToday     = 0;
            _engine.DeparturesToday   = 0;
            _engine.GoAroundsToday    = 0;
            _engine.DiversionsToday   = 0;
            _engine.ConflictCount     = 0; // Ensures yesterday's errors don't penalize today's score
            _engine.TotalDelayMinutes = 0;
            _engine.CurrentScoreGrade = "A+";
            _engine.PushAlert("🌅 New simulation day — daily stats reset.");
            _logger.LogInformation("Daily stats reset for sim day {Day}.", simDay);
        }

        _lastResetDay = simDay;
    }
}