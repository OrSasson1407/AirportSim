using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AirportSim.Server.Infrastructure.Simulation;

/// <summary>
/// Manages weather state changes independently of the physics tick.
/// Runs every 1 second (real time) to update weather transitions and storm positions.
/// Cycles weather every WeatherChangeSec simulated seconds.
/// 
/// Isolated: if this service throws and restarts, the tick loop is unaffected.
/// </summary>
public sealed class WeatherService : BackgroundService
{
    private readonly SimulationEngine           _engine;
    private readonly ILogger<WeatherService>    _logger;

    private const int WeatherChangeSec   = 300;  // sim seconds between auto-cycles
    private const int ServiceIntervalMs  = 1000; // real ms between weather ticks

    private DateTime _lastWeatherChange = DateTime.UtcNow;

    public WeatherService(SimulationEngine engine, ILogger<WeatherService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WeatherService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TickWeather();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "WeatherService: unhandled exception.");
            }

            await Task.Delay(ServiceIntervalMs, stoppingToken);
        }

        _logger.LogInformation("WeatherService stopped.");
    }

    private void TickWeather()
    {
        double simDeltaMs = ServiceIntervalMs * _engine.Clock.TimeScale;

        // ── Weather transition progress ───────────────────────────────────────
        if (_engine.WeatherTransitionRemainingMs > 0)
        {
            _engine.WeatherTransitionRemainingMs =
                Math.Max(0, _engine.WeatherTransitionRemainingMs - simDeltaMs);
        }

        // ── Storm movement ────────────────────────────────────────────────────
        if (_engine.StormCenter.HasValue)
        {
            double moveSecs = simDeltaMs / 1000.0;
            _engine.StormCenter = new AirportSim.Shared.Models.SimPoint(
                _engine.StormCenter.Value.X + _engine.StormVelocity.X * moveSecs,
                _engine.StormCenter.Value.Y + _engine.StormVelocity.Y * moveSecs);
        }

        // ── Wind shear countdown ──────────────────────────────────────────────
        if (_engine.IsWindShearActive)
        {
            _engine.WindShearRemainingMs -= simDeltaMs;
            if (_engine.WindShearRemainingMs <= 0)
            {
                _engine.IsWindShearActive = false;
                _engine.PushAlert("✅ Wind shear alert lifted. Runways open.");
                _logger.LogInformation("Wind shear cleared.");
            }
        }

        // ── Auto weather cycle ────────────────────────────────────────────────
        if ((DateTime.UtcNow - _lastWeatherChange).TotalSeconds >= WeatherChangeSec)
        {
            _engine.CycleWeather();
            _lastWeatherChange = DateTime.UtcNow;
            _logger.LogInformation("Weather auto-cycled to {Weather}.", _engine.Weather);
        }
    }
}