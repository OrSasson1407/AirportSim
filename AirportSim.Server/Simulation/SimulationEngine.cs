using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AirportSim.Server.Hubs;
using AirportSim.Shared.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace AirportSim.Server.Simulation
{
    public class SimulationEngine : BackgroundService
    {
        private readonly IHubContext<SimulationHub, ISimulationClient> _hubContext;

        public  SimClock           Clock      { get; }
        private FlightScheduler    _scheduler;
        private RunwayController   _runway;
        private GateManager        _gates;
        private ConflictDetector   _conflicts;
        private List<Aircraft>     _activeAircraft;

        private readonly Queue<Aircraft> _gateQueue = new();
        private readonly Random _rand = new();

        private readonly List<string> _pendingAudio = new();

        private WeatherCondition  _weather = WeatherCondition.Clear;
        private WeatherCondition _previousWeather = WeatherCondition.Clear;
        private double _weatherTransitionRemainingMs = 0;
        private const double WeatherTransitionDurationMs = 60000; 

        // NEW: Track RVR
        public int RvrMeters { get; private set; } = 10000;

        private bool _isWindShearActive;
        private double _windShearRemainingMs;

        private readonly List<string> _alertLog = new();
        private const int AlertLogMax = 20;

        private int _arrivalsToday;
        private int _departuresToday;
        private int _goAroundsToday;

        private const int TickIntervalMs      = 100;
        private const int BroadcastIntervalMs = 200;

        private DateTime _lastWeatherChange = DateTime.UtcNow;
        private const int WeatherChangeSec  = 300;

        public SimulationEngine(IHubContext<SimulationHub, ISimulationClient> hubContext)
        {
            _hubContext     = hubContext;
            Clock           = new SimClock(DateTime.Today.AddHours(5));
            _scheduler      = new FlightScheduler();
            _runway         = new RunwayController();
            _gates          = new GateManager();
            _conflicts      = new ConflictDetector();
            _activeAircraft = new List<Aircraft>();
        }

        public void InjectEmergency()
        {
            _scheduler.InjectEmergency(AircraftType.Medium, FlightType.Arrival);
            PushAlert("🚨 MAYDAY — manual emergency aircraft added to scheduler");
        }

        public WeatherCondition CycleWeather()
        {
            _previousWeather = _weather;
            int next = ((int)_weather + 1) % Enum.GetValues<WeatherCondition>().Length;
            _weather = (WeatherCondition)next;
            
            _weatherTransitionRemainingMs = WeatherTransitionDurationMs;
            
            // NEW: Automatically adjust RVR based on standard weather profiles
            RvrMeters = _weather switch {
                WeatherCondition.Clear => 10000,
                WeatherCondition.Cloudy => 8000,
                WeatherCondition.Rain => 3000,
                WeatherCondition.Storm => 800,
                WeatherCondition.Fog => 250, // Below CAT II minimums! Will cause diversions.
                _ => 10000
            };

            ApplyWeatherEffects();
            PushAlert($"🌤 Weather changing to {_weather}. RVR now {RvrMeters}m.");
            return _weather;
        }

        // NEW: Manual override from UI Slider
        public void SetRvr(int rvr)
        {
            RvrMeters = rvr;
            PushAlert($"👁 RVR manually set to {(rvr >= 10000 ? "10+ km" : rvr + "m")}");
        }

        public void LoadLayout(string layoutId)
        {
            _activeAircraft.Clear();
            _gateQueue.Clear();
            _gates.LoadLayout(layoutId);
            PushAlert($"🌍 Loaded airport layout: {layoutId.ToUpper()}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _scheduler.Initialize(Clock.SimulatedNow);

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
                    await BroadcastSnapshot();
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
            else if (_weather == WeatherCondition.Storm || _weather == WeatherCondition.Rain)
            {
                double chanceThreshold = (_weather == WeatherCondition.Storm) ? 300000.0 : 1200000.0;
                
                if (_rand.NextDouble() < (simDeltaMs / chanceThreshold))
                {
                    _isWindShearActive = true;
                    _windShearRemainingMs = 120_000 + _rand.NextDouble() * 90_000;
                    _runway.SetWeatherClosure(true);
                    PushAlert("🌪 WINDSHEAR / MICROBURST DETECTED! Runways closed!");
                }
            }

            var next = _scheduler.PeekNextFlight();
            if (next != null && Clock.SimulatedNow >= next.ScheduledTime)
            {
                var flightEvent = _scheduler.DequeueNextFlight();
                var aircraft    = new Aircraft(flightEvent);

                if (aircraft.State.FlightId.StartsWith("MAYDAY"))
                {
                    aircraft.DeclareEmergency("General Emergency");
                    PushAlert($"🚨 {aircraft.State.FlightId} on emergency approach");
                    _runway.DeclareEmergencyOverride(aircraft.State.FlightId);
                    AssignGate(aircraft, flightEvent.Gate);
                    _activeAircraft.Add(aircraft);
                }
                else
                {
                    aircraft.GoAroundChance = WeatherGoAroundChance(_weather);

                    if (aircraft.State.FlightType == FlightType.Departure)
                    {
                        if (!AssignGate(aircraft, flightEvent.Gate))
                        {
                            _gateQueue.Enqueue(aircraft);
                            PushAlert($"⏳ {aircraft.State.FlightId} queued — apron full");
                        }
                        else
                        {
                            _activeAircraft.Add(aircraft);
                        }
                    }
                    else
                    {
                        _activeAircraft.Add(aircraft);
                    }
                }
            }

            while (_gateQueue.Count > 0 && _gates.HasFreeGate())
            {
                var queued = _gateQueue.Dequeue();
                AssignGate(queued, queued.State.AssignedGate);
                _activeAircraft.Add(queued);
                PushAlert($"🅿 {queued.State.FlightId} gate assigned → {queued.State.AssignedGate}");
            }

            _runway.Tick(simDeltaMs);
            foreach (var alert in _runway.PendingAlerts) PushAlert(alert);
            _runway.PendingAlerts.Clear();

            foreach (var ac in _activeAircraft.ToList())
            {
                var phaseBefore = ac.State.Phase;
                var statusBefore = ac.State.Status;
                
                // NEW: Pass RvrMeters to the Aircraft Tick
                ac.Tick(simDeltaMs, _runway, _weather, RvrMeters);

                if (statusBefore != AircraftStatus.Emergency && ac.State.Status == AircraftStatus.Emergency)
                {
                    _pendingAudio.Add("alarm_emergency.wav");
                }

                if (phaseBefore != ac.State.Phase)
                {
                    switch (ac.State.Phase)
                    {
                        case AircraftPhase.Approaching:
                            _pendingAudio.Add("atc_cleared.wav");
                            break;
                        case AircraftPhase.GoAround:
                            _pendingAudio.Add("atc_go_around.wav");
                            break;
                        case AircraftPhase.Taxiing:
                            _pendingAudio.Add("engine_light.wav");
                            break;
                        case AircraftPhase.Takeoff:
                            _pendingAudio.Add("engine_heavy.wav");
                            break;
                    }
                }

                if (statusBefore != AircraftStatus.Emergency && ac.State.Status == AircraftStatus.Emergency)
                {
                    PushAlert($"🚨 EMERGENCY DECLARED: {ac.State.FlightId} ({ac.State.EmergencyReason})");
                    _runway.DeclareEmergencyOverride(ac.State.FlightId);
                }

                if (statusBefore != AircraftStatus.Diverting && ac.State.Status == AircraftStatus.Diverting)
                {
                    PushAlert($"↩️ DIVERSION DECLARED: {ac.State.FlightId} proceeding to alternate. Reason: {ac.State.EmergencyReason}");
                }

                if (phaseBefore == AircraftPhase.Taxiing &&
                    ac.State.Phase == AircraftPhase.Parked &&
                    ac.State.FlightType == FlightType.Arrival)
                {
                    if (!AssignGate(ac, ac.State.AssignedGate))
                        PushAlert($"⚠ {ac.State.FlightId} parked but no gate free");
                    else
                        PushAlert($"🅿 {ac.State.FlightId} parked at gate {ac.State.AssignedGate}");
                }

                if (phaseBefore == AircraftPhase.Parked &&
                    ac.State.Phase == AircraftPhase.Taxiing &&
                    ac.State.FlightType == FlightType.Departure)
                {
                    _gates.Release(ac.State.FlightId);
                    PushAlert($"🚪 {ac.State.FlightId} pushed back from {ac.State.AssignedGate}");
                }

                if (phaseBefore == AircraftPhase.OnFinal &&
                    ac.State.Phase == AircraftPhase.GoAround)
                {
                    _goAroundsToday++;
                    _conflicts.RecordGoAround(simNowMs);
                    
                    if (ac.LastGoAroundWasWeatherForced)
                    {
                        PushAlert($"🌫️ {ac.State.FlightId} ({ac.State.Type}) aborted: Below approach minimums");
                    }
                    else
                    {
                        PushAlert($"↩ {ac.State.FlightId} executing go-around (attempt {ac.State.GoAroundCount})");
                    }
                }

                if (phaseBefore == AircraftPhase.Rollout &&
                    ac.State.Phase == AircraftPhase.Taxiing)
                {
                    _arrivalsToday++;
                    PushAlert($"✅ {ac.State.FlightId} landed and vacated 28L");
                    
                    if (ac.State.Status == AircraftStatus.Emergency)
                    {
                        PushAlert($"🟢 {ac.State.FlightId} is safe on taxiway. Lifting airfield lockdown.");
                        _runway.SetEmergencyLockdown(false);
                    }
                }

                if (phaseBefore == AircraftPhase.Climbing &&
                    ac.State.Phase == AircraftPhase.Departed)
                {
                    _departuresToday++;
                    _gates.Release(ac.State.FlightId);
                    PushAlert($"✈ {ac.State.FlightId} departed via 28R");
                }

                if (ac.IsFinished)
                {
                    _gates.Release(ac.State.FlightId);
                    _activeAircraft.Remove(ac);
                    
                    if (ac.State.Phase == AircraftPhase.Diverted)
                    {
                        PushAlert($"🛬 {ac.State.FlightId} has successfully diverted out of local airspace.");
                    }
                }
            }

            var states = _activeAircraft.Select(a => a.State).ToList();
            _conflicts.Check(states, simNowMs, simDeltaMs);
            foreach (var alert in _conflicts.PendingAlerts)
                PushAlert(alert);
        }

        private bool AssignGate(Aircraft aircraft, string preferred)
        {
            var result = _gates.Assign(aircraft.State.FlightId, preferred);
            if (result == null) return false;

            aircraft.State.AssignedGate = result.Value.gateName;
            aircraft.State.GateX        = result.Value.gateX;
            aircraft.State.GateY        = result.Value.gateY;
            return true;
        }

        private async Task BroadcastSnapshot()
        {
            var snapshot = new SimSnapshot
            {
                SimulatedTime       = Clock.SimulatedNow,
                TimeScale           = Clock.TimeScale,
                IsPaused            = Clock.IsPaused,
                Runways             = _runway.GetSnapshots(),
                ActiveAircraft      = _activeAircraft.Select(a => a.State).ToList(),
                QueuedFlights       = _scheduler.GetQueuePreview(5),
                Weather             = _weather,
                PreviousWeather     = _previousWeather,
                WeatherTransitionProgress = 1.0 - (_weatherTransitionRemainingMs / WeatherTransitionDurationMs),
                IsWindShearActive   = _isWindShearActive,
                RvrMeters           = this.RvrMeters, // NEW
                RecentAlerts        = _alertLog.TakeLast(5).Reverse().ToList(),
                TotalArrivalsToday  = _arrivalsToday,
                TotalDeparturesToay = _departuresToday,
                GoAroundsToday      = _goAroundsToday
            };

            await _hubContext.Clients.All.ReceiveSnapshot(snapshot);

            if (_pendingAudio.Any())
            {
                await _hubContext.Clients.All.ReceiveAudioTriggers(_pendingAudio.ToList());
                _pendingAudio.Clear();
            }
        }

        private void PushAlert(string message)
        {
            _alertLog.Add($"[{Clock.SimulatedNow:HH:mm}] {message}");
            if (_alertLog.Count > AlertLogMax)
                _alertLog.RemoveAt(0);
        }

        private static double WeatherGoAroundChance(WeatherCondition weather) => weather switch
        {
            WeatherCondition.Clear  => 0.05,
            WeatherCondition.Cloudy => 0.10,
            WeatherCondition.Rain   => 0.18,
            WeatherCondition.Fog    => 0.35,
            WeatherCondition.Storm  => 0.55,
            _                       => 0.08
        };

        private void ApplyWeatherEffects()
        {
            double chance = WeatherGoAroundChance(_weather);
            foreach (var ac in _activeAircraft)
                if (ac.State.Status == AircraftStatus.Normal)
                    ac.GoAroundChance = chance;
        }
    }
}