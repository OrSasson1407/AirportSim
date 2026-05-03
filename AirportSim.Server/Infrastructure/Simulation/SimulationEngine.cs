using AirportSim.Server.Application.Commands;
using AirportSim.Server.Application.Queries;
using AirportSim.Server.Domain.Interfaces;
using AirportSim.Shared.Models;
using MediatR;

namespace AirportSim.Server.Infrastructure.Simulation;

/// <summary>
/// Pure simulation state machine.
/// No longer a BackgroundService — the three hosted services drive it.
/// This class owns all in-memory state and exposes a thread-safe Tick() method.
/// </summary>
public class SimulationEngine : ISimulationService
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly IBroadcastService _broadcast;
    private readonly IMediator         _mediator;
    private readonly ICacheService     _cache;

    // ── Domain objects ────────────────────────────────────────────────────────
    public  SimClock             Clock           { get; }
    private FlightScheduler      _scheduler;
    private RunwayController     _runway;
    private GateManager          _gates;
    private ConflictDetector     _conflicts;
    private GroundVehicleManager _groundVehicles;
    private List<Aircraft>       _activeAircraft;

public int ConflictCount { get; set; }
    private readonly Queue<Aircraft> _gateQueue    = new();
    private readonly Random          _rand         = new();
    private readonly List<string>    _pendingAudio = new();
    private readonly object          _tickLock     = new();

    // ── Weather state (read/written by WeatherService) ────────────────────────
    public WeatherCondition Weather         { get; private set; } = WeatherCondition.Clear;
    public WeatherCondition PreviousWeather { get; private set; } = WeatherCondition.Clear;
    public double WeatherTransitionRemainingMs { get; set; } = 0;
    public const double WeatherTransitionDurationMs = 60000;
    public int     RvrMeters          { get; set; } = 10000;
    public bool    IsWindShearActive  { get; set; }
    public double  WindShearRemainingMs { get; set; }
    public SimPoint? StormCenter      { get; set; }
    public SimPoint  StormVelocity    { get; set; }

    // ── Metrics state (read/written by MetricsService) ────────────────────────
    public int ArrivalsToday    { get; set; }
    public int DeparturesToday  { get; set; }
    public int GoAroundsToday   { get; set; }
    public int DiversionsToday  { get; set; }
    public int TotalDelayMinutes { get; set; }
    public string CurrentScoreGrade { get; set; } = "A+";

    // ── Internal state ────────────────────────────────────────────────────────
    private readonly List<string>    _alertLog = new();
    private const int AlertLogMax = 20;

    private char   _atisLetter  = 'A';
    private string _currentAtis = string.Empty;

    private readonly LinkedList<SimSnapshot> _snapshotHistory = new();
    private const int MaxHistoryFrames = 600;

    public SimulationEngine(IBroadcastService broadcast, IMediator mediator, ICacheService cache)
    {
        _broadcast      = broadcast;
        _mediator       = mediator;
        _cache          = cache;
        Clock           = new SimClock(DateTime.Today.AddHours(5));
        _scheduler      = new FlightScheduler();
        _runway         = new RunwayController();
        _gates          = new GateManager();
        _conflicts      = new ConflictDetector();
        _groundVehicles = new GroundVehicleManager();
        _activeAircraft = new List<Aircraft>();
    }

    public void Initialize()
    {
        _scheduler.Initialize(Clock.SimulatedNow);
        GenerateAtis();
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
        PreviousWeather = Weather;
        Weather = (WeatherCondition)(((int)Weather + 1) % Enum.GetValues<WeatherCondition>().Length);
        WeatherTransitionRemainingMs = WeatherTransitionDurationMs;

        RvrMeters = Weather switch
        {
            WeatherCondition.Clear  => 10000,
            WeatherCondition.Cloudy => 8000,
            WeatherCondition.Rain   => 3000,
            WeatherCondition.Storm  => 800,
            WeatherCondition.Fog    => 250,
            _                       => 10000
        };

        if (Weather is WeatherCondition.Storm or WeatherCondition.Rain)
        {
            StormCenter   = new SimPoint(-200, _rand.Next(100, 500));
            StormVelocity = new SimPoint(40 + _rand.NextDouble() * 30,
                                         (_rand.NextDouble() - 0.5) * 20);
        }
        else StormCenter = null;

        ApplyWeatherEffects();
        GenerateAtis();
        PushAlert($"🌤 Weather changing to {Weather}. RVR now {RvrMeters}m.");
        return Weather;
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
        _ = _cache.RemoveAsync(CacheKeys.LayoutPrefix + layoutId);
        PushAlert($"🌍 Loaded airport layout: {layoutId.ToUpper()}");
    }

    // ── Core tick — called by SimulationTickService ───────────────────────────

    public void Tick(int realElapsedMs)
    {
        lock (_tickLock)
        {
            Clock.Tick(realElapsedMs);
            _scheduler.Update(Clock.SimulatedNow);

            double simDeltaMs = realElapsedMs * Clock.TimeScale;
            double simNowMs   = Clock.SimulatedNow.Subtract(DateTime.Today).TotalMilliseconds;

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
                PushAlert($"📡 ATC: {cleared.State.FlightId} cleared from holding stack.");
            }

            // Spawn next flight
            var next = _scheduler.PeekNextFlight();
            if (next != null && Clock.SimulatedNow >= next.ScheduledTime)
            {
                var      flightEvent  = _scheduler.DequeueNextFlight();
                RunwayId targetRunway = _runway.GetBestRunway(flightEvent.FlightType);
                var      aircraft     = new Aircraft(flightEvent, targetRunway);

                string? gate = _gates.AssignGate(aircraft.State.FlightId, aircraft.State.Type);
                if (gate != null)
                {
                    aircraft.State.AssignedGate    = gate;
                    (double gx, double gy)         = _gates.GetGatePosition(gate);
                    aircraft.State.GateX           = gx;
                    aircraft.State.GateY           = gy;
                }

                TotalDelayMinutes += flightEvent.DelayMinutes;
                if (flightEvent.DelayMinutes > 0)
                    PushAlert($"⏱ {flightEvent.FlightId}: delay of {flightEvent.DelayMinutes} min.");

                _activeAircraft.Add(aircraft);

                if (aircraft.State.Status == AircraftStatus.Emergency)
                {
                    _runway.DeclareEmergencyOverride(aircraft.State.FlightId, targetRunway);
                    _pendingAudio.Add("alarm_emergency.wav");
                    PushAlert($"🚨 MAYDAY: {aircraft.State.FlightId} — priority landing.");
                }
            }

            // Tick aircraft
            foreach (var ac in _activeAircraft)
            {
                bool wasParked   = ac.State.Phase == AircraftPhase.Parked;
                bool wasClimbing = ac.State.Phase == AircraftPhase.Climbing;
                bool wasDiverted = ac.State.Phase == AircraftPhase.Diverted;

                ac.Tick(simDeltaMs, _runway, _gates, Weather, RvrMeters);

                if (ac.State.FlightType == FlightType.Arrival
                    && !wasParked
                    && ac.State.Phase == AircraftPhase.Parked
                    && ac.State.Turnaround == TurnaroundPhase.Deplaning)
                {
                    ArrivalsToday++;
                    _ = _mediator.Send(new LogCompletedFlightCommand(
                        ac.State.FlightId, ac.State.Type.ToString(), "Arrival",
                        ac.State.Origin, ac.State.Destination, ac.State.AssignedGate,
                        "Landed", ac.State.GoAroundCount, ac.State.DelayMinutes,
                        ac.State.CurrentFuelPercent, Clock.SimulatedNow));
                }

                if (ac.State.FlightType == FlightType.Departure
                    && !wasClimbing
                    && ac.State.Phase == AircraftPhase.Climbing)
                {
                    DeparturesToday++;
                    _pendingAudio.Add("atc_cleared.wav");
                    PushAlert($"✈ {ac.State.FlightId}: airborne to {ac.State.Destination}.");
                    _ = _mediator.Send(new LogCompletedFlightCommand(
                        ac.State.FlightId, ac.State.Type.ToString(), "Departure",
                        ac.State.Origin, ac.State.Destination, ac.State.AssignedGate,
                        "Departed", ac.State.GoAroundCount, ac.State.DelayMinutes,
                        ac.State.CurrentFuelPercent, Clock.SimulatedNow));
                }

                if (!wasDiverted && ac.State.Phase == AircraftPhase.Diverted)
                {
                    DiversionsToday++;
                    PushAlert($"🔀 {ac.State.FlightId}: DIVERTED — fuel critical.");
                    _ = _mediator.Send(new LogCompletedFlightCommand(
                        ac.State.FlightId, ac.State.Type.ToString(), ac.State.FlightType.ToString(),
                        ac.State.Origin, ac.State.Destination, ac.State.AssignedGate,
                        "Diverted", ac.State.GoAroundCount, ac.State.DelayMinutes,
                        ac.State.CurrentFuelPercent, Clock.SimulatedNow));
                }

                if (ac.State.FlightType == FlightType.Arrival
                    && ac.State.Phase == AircraftPhase.GoAround
                    && ac.LastGoAroundWasWeatherForced)
                {
                    GoAroundsToday++;
                    _pendingAudio.Add("atc_go_around.wav");
                    PushAlert($"↩ {ac.State.FlightId}: GO-AROUND.");
                }

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
    }

    // ── Broadcast — called by SimulationTickService ───────────────────────────

    public async Task BroadcastAsync(CancellationToken ct)
    {
        SimSnapshot snapshot;
        List<string> audio;

        lock (_tickLock)
        {
            snapshot = BuildSnapshot();
            _snapshotHistory.AddLast(snapshot);
            if (_snapshotHistory.Count > MaxHistoryFrames)
                _snapshotHistory.RemoveFirst();

            audio = new List<string>(_pendingAudio);
            _pendingAudio.Clear();
        }

        if (audio.Count > 0)
            await _broadcast.BroadcastAudioTriggersAsync(audio, ct);

        _ = _cache.SetAsync(CacheKeys.LatestSnapshot, snapshot, TimeSpan.FromSeconds(2), ct);

        await _broadcast.BroadcastSnapshotAsync(snapshot, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public void PushAlert(string msg)
    {
        _alertLog.Insert(0, msg);
        if (_alertLog.Count > AlertLogMax) _alertLog.RemoveAt(_alertLog.Count - 1);
    }

    public void ApplyWeatherEffects()
    {
        foreach (var ac in _activeAircraft)
            ac.GoAroundChance = Weather switch
            {
                WeatherCondition.Clear  => 0.05,
                WeatherCondition.Cloudy => 0.10,
                WeatherCondition.Rain   => 0.20,
                WeatherCondition.Fog    => 0.35,
                WeatherCondition.Storm  => 0.50,
                _                       => 0.08
            };
    }

    public void GenerateAtis()
    {
        string timeZ  = Clock.SimulatedNow.ToString("HHmm");
        string wind   = Weather switch
        {
            WeatherCondition.Clear  => "VARIABLE AT 4",
            WeatherCondition.Cloudy => "270 AT 12",
            WeatherCondition.Rain   => "240 AT 18",
            WeatherCondition.Storm  => "210 AT 35 GUSTING 45",
            WeatherCondition.Fog    => "CALM",
            _                       => "000 AT 0"
        };
        string vis    = RvrMeters >= 10000 ? "10 KILOMETERS OR MORE" : $"{RvrMeters} METERS";
        string clouds = Weather switch
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
        Weather                   = Weather,
        PreviousWeather           = PreviousWeather,
        WeatherTransitionProgress = WeatherTransitionRemainingMs > 0
                                    ? 1.0 - (WeatherTransitionRemainingMs / WeatherTransitionDurationMs)
                                    : 1.0,
        IsWindShearActive         = IsWindShearActive,
        RvrMeters                 = RvrMeters,
        StormCenter               = StormCenter,
        CurrentAtis               = _currentAtis,
        RecentAlerts              = new List<string>(_alertLog),
        TotalArrivalsToday        = ArrivalsToday,
        TotalDeparturesToay       = DeparturesToday,
        GoAroundsToday            = GoAroundsToday,
        DiversionsToday           = DiversionsToday,
        ConflictCountToday        = _conflicts.TotalConflicts,
        TotalDelayMinutes         = TotalDelayMinutes,
        CurrentScoreGrade         = CurrentScoreGrade
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
}