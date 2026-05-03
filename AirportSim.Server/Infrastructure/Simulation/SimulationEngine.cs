using AirportSim.Server.Application.Commands;
using AirportSim.Server.Domain.Interfaces;
using AirportSim.Shared.Models;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AirportSim.Server.Infrastructure.Simulation;

public class SimulationEngine : BackgroundService, ISimulationService
{
    private readonly IBroadcastService _broadcast;
    private readonly IMediator         _mediator;

    public  SimClock             Clock           { get; }
    private FlightScheduler      _scheduler;
    private RunwayController     _runway;
    private GateManager          _gates;
    private ConflictDetector     _conflicts;
    private GroundVehicleManager _groundVehicles;
    private List<Aircraft>       _activeAircraft;

    private readonly Queue<Aircraft> _gateQueue    = new();
    private readonly Random          _rand         = new();
    private readonly List<string>    _pendingAudio = new();

    private WeatherCondition _weather         = WeatherCondition.Clear;
    private WeatherCondition _previousWeather = WeatherCondition.Clear;
    private double _weatherTransitionRemainingMs = 0;
    private const double WeatherTransitionDurationMs = 60000;

    public int RvrMeters { get; private set; } = 10000;

    private bool   _isWindShearActive;
    private double _windShearRemainingMs;
    private SimPoint? _stormCenter;
    private SimPoint  _stormVelocity;

    private readonly List<string> _alertLog = new();
    private const int AlertLogMax = 20;

    private int _arrivalsToday;
    private int _departuresToday;
    private int _goAroundsToday;
    private int _diversionsToday;
    private int _totalDelayMinutes;

    private const int TickIntervalMs      = 100;
    private const int BroadcastIntervalMs = 200;

    private DateTime _lastWeatherChange = DateTime.UtcNow;
    private const int WeatherChangeSec  = 300;

    private char   _atisLetter  = 'A';
    private string _currentAtis = string.Empty;

    private readonly LinkedList<SimSnapshot> _snapshotHistory = new();
    private const int MaxHistoryFrames = 600;

    public SimulationEngine(IBroadcastService broadcast, IMediator mediator)
    {
        _broadcast      = broadcast;
        _mediator       = mediator;
        Clock           = new SimClock(DateTime.Today.AddHours(5));
        _scheduler      = new FlightScheduler();
        _runway         = new RunwayController();
        _gates          = new GateManager();
        _conflicts      = new ConflictDetector();
        _groundVehicles = new GroundVehicleManager();
        _activeAircraft = new List<Aircraft>();
    }

    // ── ISimulationService ────────────────────────────────────────────────────

    public void ToggleOpsMode(RunwayOpsMode mode) => _runway.SetOpsMode(mode);

    public void InjectEmergency()
    {
        _scheduler.InjectEmergency(AircraftType.Medium, FlightType.Arrival);
        PushAlert("🚨 MAYDAY — manual emergency aircraft added to scheduler");
    }

    public void GrantClearance(string flightId, string clearanceType)
    {
        var ac = _activeAircraft.FirstOrDefault(a => a.State.FlightId == flightId);
        ac?.GrantClearance(clearanceType);
    }

    public void AssignSpeed(string flightId, int speedKts)
    {
        var ac = _activeAircraft.FirstOrDefault(a => a.State.FlightId == flightId);
        ac?.AssignSpeed(speedKts);
    }

    public void AssignAltitude(string flightId, int altitudeFt)
    {
        var ac = _activeAircraft.FirstOrDefault(a => a.State.FlightId == flightId);
        ac?.AssignAltitude(altitudeFt);
    }

    public List<SimSnapshot> GetReplayBuffer()  => _snapshotHistory.ToList();
    public SimSnapshot? GetLatestSnapshot()     => _snapshotHistory.Last?.Value;

    public WeatherCondition CycleWeather()
    {
        _previousWeather = _weather;
        _weather = (WeatherCondition)(((int)_weather + 1) % Enum.GetValues<WeatherCondition>().Length);
        _weatherTransitionRemainingMs = WeatherTransitionDurationMs;

        RvrMeters = _weather switch
        {
            WeatherCondition.Clear  => 10000,
            WeatherCondition.Cloudy => 8000,
            WeatherCondition.Rain   => 3000,
            WeatherCondition.Storm  => 800,
            WeatherCondition.Fog    => 250,
            _                       => 10000
        };

        if (_weather is WeatherCondition.Storm or WeatherCondition.Rain)
        {
            _stormCenter   = new SimPoint(-200, _rand.Next(100, 500));
            _stormVelocity = new SimPoint(40 + _rand.NextDouble() * 30,
                                          (_rand.NextDouble() - 0.5) * 20);
        }
        else _stormCenter = null;

        ApplyWeatherEffects();
        GenerateAtis();
        PushAlert($"🌤 Weather changing to {_weather}. RVR now {RvrMeters}m.");
        return _weather;
    }

    public void SetRvr(int rvr)
    {
        RvrMeters = rvr;
        GenerateAtis();
        PushAlert($"👁 RVR manually set to {(rvr >= 10000 ? "10+ km" : rvr + "m")}");
    }

    public void LoadLayout(string layoutId)
    {
        _activeAircraft.Clear();
        _gateQueue.Clear();
        _gates.LoadLayout(layoutId);
        _groundVehicles.Initialize(_gates);
        GenerateAtis();
        PushAlert($"🌍 Loaded airport layout: {layoutId.ToUpper()}");
    }

    // ── BackgroundService loop ────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _scheduler.Initialize(Clock.SimulatedNow);
        GenerateAtis();

        DateTime lastBroadcast = DateTime.UtcNow;
        DateTime lastTick      = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime now       = DateTime.UtcNow;
            int      elapsedMs = (int)(now - lastTick).TotalMilliseconds;

            if (elapsedMs >= TickIntervalMs)
            {
                UpdateSimulation(elapsedMs);
                lastTick = now;
            }

            if ((now - lastBroadcast).TotalMilliseconds >= BroadcastIntervalMs)
            {
                await BroadcastSnapshotAsync(stoppingToken);
                lastBroadcast = now;
            }

            if ((now - _lastWeatherChange).TotalSeconds >= WeatherChangeSec)
            {
                CycleWeather();
                _lastWeatherChange = now;
            }

            await Task.Delay(10, stoppingToken);
        }
    }

    // ── Simulation tick ───────────────────────────────────────────────────────

    private void UpdateSimulation(int realElapsedMs)
    {
        Clock.Tick(realElapsedMs);
        _scheduler.Update(Clock.SimulatedNow);

        double simDeltaMs = realElapsedMs * Clock.TimeScale;
        double simNowMs   = Clock.SimulatedNow.Subtract(DateTime.Today).TotalMilliseconds;

        if (_weatherTransitionRemainingMs > 0)
        {
            _weatherTransitionRemainingMs -= simDeltaMs;
            if (_weatherTransitionRemainingMs < 0) _weatherTransitionRemainingMs = 0;
        }

        if (_stormCenter.HasValue)
        {
            double moveSecs = simDeltaMs / 1000.0;
            _stormCenter = new SimPoint(
                _stormCenter.Value.X + _stormVelocity.X * moveSecs,
                _stormCenter.Value.Y + _stormVelocity.Y * moveSecs);
        }

        if (_isWindShearActive)
        {
            _windShearRemainingMs -= simDeltaMs;
            if (_windShearRemainingMs <= 0)
            {
                _isWindShearActive = false;
                _runway.SetWeatherClosure(false);
                PushAlert("✅ Wind shear alert lifted. Runways open.");
            }
        }
        else if (_weather is WeatherCondition.Storm or WeatherCondition.Rain)
        {
            double threshold = _weather == WeatherCondition.Storm ? 300000.0 : 1200000.0;
            if (_rand.NextDouble() < (simDeltaMs / threshold))
            {
                _isWindShearActive    = true;
                _windShearRemainingMs = 120_000 + _rand.NextDouble() * 90_000;
                _runway.SetWeatherClosure(true);
                PushAlert("🌪 WINDSHEAR / MICROBURST DETECTED! Runways closed!");
            }
        }

        _groundVehicles.Tick(simDeltaMs);

        // Holding stack
        var holdingArrivals = _activeAircraft
            .Where(a => a.State.FlightType == FlightType.Arrival
                     && a.State.Phase == AircraftPhase.Holding)
            .OrderByDescending(a => a.State.GoAroundCount)
            .ThenBy(a => a.State.FlightId)
            .ToList();

        int approachingCount = _activeAircraft.Count(a =>
            a.State.FlightType == FlightType.Arrival &&
            a.State.Phase is AircraftPhase.Approaching or AircraftPhase.OnFinal);

        if (approachingCount < 2 && holdingArrivals.Any())
        {
            var cleared = holdingArrivals.First();
            cleared.ClearFromHold();
            PushAlert($"📡 ATC: {cleared.State.FlightId} cleared from holding stack, resuming approach.");
        }

        // Spawn next scheduled flight
        var next = _scheduler.PeekNextFlight();
        if (next != null && Clock.SimulatedNow >= next.ScheduledTime)
        {
            var      flightEvent  = _scheduler.DequeueNextFlight();
            RunwayId targetRunway = _runway.GetBestRunway(flightEvent.FlightType);
            var      aircraft     = new Aircraft(flightEvent, targetRunway);

            string? gate = _gates.AssignGate(aircraft.State.FlightId, aircraft.State.Type);
            if (gate != null)
            {
                aircraft.State.AssignedGate = gate;
                // ── FIXED: explicit types on deconstruction ────────────────
                (double gx, double gy) = _gates.GetGatePosition(gate);
                aircraft.State.GateX = gx;
                aircraft.State.GateY = gy;
            }

            _totalDelayMinutes += flightEvent.DelayMinutes;
            if (flightEvent.DelayMinutes > 0)
                PushAlert($"⏱ {flightEvent.FlightId}: pre-departure delay of {flightEvent.DelayMinutes} min.");

            _activeAircraft.Add(aircraft);

            if (aircraft.State.Status == AircraftStatus.Emergency)
            {
                _runway.DeclareEmergencyOverride(aircraft.State.FlightId, targetRunway);
                _pendingAudio.Add("alarm_emergency.wav");
                PushAlert($"🚨 MAYDAY: {aircraft.State.FlightId} declaring emergency — priority landing.");
            }
        }

        // Tick each aircraft + detect completion events
        foreach (var ac in _activeAircraft)
        {
            bool wasParked   = ac.State.Phase == AircraftPhase.Parked;
            bool wasClimbing = ac.State.Phase == AircraftPhase.Climbing;
            bool wasDiverted = ac.State.Phase == AircraftPhase.Diverted;

            ac.Tick(simDeltaMs, _runway, _gates, _weather, RvrMeters);

            // Arrival landed
            if (ac.State.FlightType == FlightType.Arrival
                && !wasParked
                && ac.State.Phase == AircraftPhase.Parked
                && ac.State.Turnaround == TurnaroundPhase.Deplaning)
            {
                _arrivalsToday++;
                _ = _mediator.Send(new LogCompletedFlightCommand(
                    ac.State.FlightId, ac.State.Type.ToString(), "Arrival",
                    ac.State.Origin, ac.State.Destination, ac.State.AssignedGate,
                    "Landed", ac.State.GoAroundCount, ac.State.DelayMinutes,
                    ac.State.CurrentFuelPercent, Clock.SimulatedNow));
            }

            // Departure airborne
            if (ac.State.FlightType == FlightType.Departure
                && !wasClimbing
                && ac.State.Phase == AircraftPhase.Climbing)
            {
                _departuresToday++;
                _pendingAudio.Add("atc_cleared.wav");
                PushAlert($"✈ {ac.State.FlightId}: airborne to {ac.State.Destination}.");
                _ = _mediator.Send(new LogCompletedFlightCommand(
                    ac.State.FlightId, ac.State.Type.ToString(), "Departure",
                    ac.State.Origin, ac.State.Destination, ac.State.AssignedGate,
                    "Departed", ac.State.GoAroundCount, ac.State.DelayMinutes,
                    ac.State.CurrentFuelPercent, Clock.SimulatedNow));
            }

            // Diversion
            if (!wasDiverted && ac.State.Phase == AircraftPhase.Diverted)
            {
                _diversionsToday++;
                PushAlert($"🔀 {ac.State.FlightId}: DIVERTED — fuel critical.");
                _ = _mediator.Send(new LogCompletedFlightCommand(
                    ac.State.FlightId, ac.State.Type.ToString(), ac.State.FlightType.ToString(),
                    ac.State.Origin, ac.State.Destination, ac.State.AssignedGate,
                    "Diverted", ac.State.GoAroundCount, ac.State.DelayMinutes,
                    ac.State.CurrentFuelPercent, Clock.SimulatedNow));
            }

            // Go-around audio
            if (ac.State.FlightType == FlightType.Arrival
                && ac.State.Phase == AircraftPhase.GoAround
                && ac.LastGoAroundWasWeatherForced)
            {
                _goAroundsToday++;
                _pendingAudio.Add("atc_go_around.wav");
                PushAlert($"↩ {ac.State.FlightId}: GO-AROUND (weather/separation).");
            }

            // Emergency lockdown lift
            if (ac.State.Status == AircraftStatus.Emergency
                && ac.State.Phase == AircraftPhase.Parked)
                _runway.SetEmergencyLockdown(false);
        }

        _runway.Tick(simDeltaMs);
        foreach (var alert in _runway.PendingAlerts) PushAlert(alert);
        _runway.PendingAlerts.Clear();

        _conflicts.Check(_activeAircraft.Select(a => a.State).ToList(), simNowMs, simDeltaMs);
        foreach (var alert in _conflicts.PendingAlerts) PushAlert(alert);

        _activeAircraft.RemoveAll(a => a.IsFinished);
    }

    // ── Broadcast ─────────────────────────────────────────────────────────────

    private async Task BroadcastSnapshotAsync(CancellationToken ct)
    {
        var snapshot = BuildSnapshot();
        _snapshotHistory.AddLast(snapshot);
        if (_snapshotHistory.Count > MaxHistoryFrames)
            _snapshotHistory.RemoveFirst();

        if (_pendingAudio.Count > 0)
        {
            await _broadcast.BroadcastAudioTriggersAsync(new List<string>(_pendingAudio), ct);
            _pendingAudio.Clear();
        }

        await _broadcast.BroadcastSnapshotAsync(snapshot, ct);
    }

    private SimSnapshot BuildSnapshot() => new()
    {
        SimulatedTime             = Clock.SimulatedNow,
        TimeScale                 = Clock.TimeScale,
        IsPaused                  = Clock.IsPaused,
        Runways                   = _runway.GetSnapshots(),
        ActiveAircraft            = _activeAircraft.Select(a => a.State).ToList(),
        GroundVehicles            = _groundVehicles.GetSnapshots(),
        QueuedFlights             = _scheduler.GetQueuePreview(),
        DepartureBoard            = BuildDepartureBoard(),
        Weather                   = _weather,
        PreviousWeather           = _previousWeather,
        WeatherTransitionProgress = _weatherTransitionRemainingMs > 0
                                    ? 1.0 - (_weatherTransitionRemainingMs / WeatherTransitionDurationMs)
                                    : 1.0,
        IsWindShearActive         = _isWindShearActive,
        RvrMeters                 = RvrMeters,
        StormCenter               = _stormCenter,
        CurrentAtis               = _currentAtis,
        RecentAlerts              = new List<string>(_alertLog),
        TotalArrivalsToday        = _arrivalsToday,
        TotalDeparturesToay       = _departuresToday,
        GoAroundsToday            = _goAroundsToday,
        DiversionsToday           = _diversionsToday,
        ConflictCountToday        = _conflicts.TotalConflicts,
        TotalDelayMinutes         = _totalDelayMinutes,
        CurrentScoreGrade         = ComputeScoreGrade()
    };

    private List<FidsEntry> BuildDepartureBoard() =>
        _activeAircraft
            .Where(a => a.State.FlightType == FlightType.Departure)
            .Select(a => new FidsEntry
            {
                FlightId    = a.State.FlightId,
                Destination = a.State.Destination,
                Gate        = a.State.AssignedGate,
                Status      = a.State.Phase switch
                {
                    AircraftPhase.Parked   => a.State.Turnaround == TurnaroundPhase.Ready
                                             ? "BOARDING" : a.State.Turnaround.ToString().ToUpper(),
                    AircraftPhase.Pushback => "PUSHBACK",
                    AircraftPhase.Taxiing  => "TAXIING",
                    AircraftPhase.Holding  => "HOLDING",
                    AircraftPhase.Takeoff  => "TAKEOFF",
                    AircraftPhase.Climbing => "AIRBORNE",
                    AircraftPhase.Departed => "DEPARTED",
                    _                      => a.State.Phase.ToString().ToUpper()
                }
            })
            .ToList();

    private string ComputeScoreGrade()
    {
        int score = Math.Max(0, 100
            - _goAroundsToday  * 2
            - _diversionsToday * 10
            - _conflicts.TotalConflicts * 5
            - _totalDelayMinutes / 10);
        return score switch
        {
            >= 95 => "A+", >= 90 => "A", >= 85 => "B+",
            >= 80 => "B",  >= 70 => "C", >= 60 => "D",
            _     => "F"
        };
    }

    private void PushAlert(string msg)
    {
        _alertLog.Insert(0, msg);
        if (_alertLog.Count > AlertLogMax) _alertLog.RemoveAt(_alertLog.Count - 1);
    }

    private void ApplyWeatherEffects()
    {
        foreach (var ac in _activeAircraft)
            ac.GoAroundChance = _weather switch
            {
                WeatherCondition.Clear  => 0.05,
                WeatherCondition.Cloudy => 0.10,
                WeatherCondition.Rain   => 0.20,
                WeatherCondition.Fog    => 0.35,
                WeatherCondition.Storm  => 0.50,
                _                       => 0.08
            };
    }

    private void GenerateAtis()
    {
        string timeZ  = Clock.SimulatedNow.ToString("HHmm");
        string wind   = _weather switch
        {
            WeatherCondition.Clear  => "VARIABLE AT 4",
            WeatherCondition.Cloudy => "270 AT 12",
            WeatherCondition.Rain   => "240 AT 18",
            WeatherCondition.Storm  => "210 AT 35 GUSTING 45",
            WeatherCondition.Fog    => "CALM",
            _                       => "000 AT 0"
        };
        string vis    = RvrMeters >= 10000 ? "10 KILOMETERS OR MORE" : $"{RvrMeters} METERS";
        string clouds = _weather switch
        {
            WeatherCondition.Clear  => "CAVOK",
            WeatherCondition.Cloudy => "BROKEN 3000 OVERCAST 8000",
            WeatherCondition.Rain   => "SCATTERED 1500 OVERCAST 4000",
            WeatherCondition.Storm  => "FEW 1000 CUMULONIMBUS BROKEN 2000",
            WeatherCondition.Fog    => "VERTICAL VISIBILITY 100",
            _                       => "CAVOK"
        };
        int qnh = 1013 + _rand.Next(-10, 10);
        _currentAtis =
            $"AIRPORT INFORMATION {_atisLetter}. {timeZ} ZULU. WIND {wind}. " +
            $"VISIBILITY {vis}. {clouds}. ALTIMETER {qnh}. " +
            $"ARRIVING RUNWAY 28L. DEPARTING RUNWAY 28R. " +
            $"ADVISE ON INITIAL CONTACT YOU HAVE INFORMATION {_atisLetter}.";
        _atisLetter++;
        if (_atisLetter > 'Z') _atisLetter = 'A';
    }
}