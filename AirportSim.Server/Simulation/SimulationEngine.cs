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
        private GroundVehicleManager _groundVehicles; 
        private List<Aircraft>     _activeAircraft;

        private readonly Queue<Aircraft> _gateQueue = new();
        private readonly Random _rand = new();

        private readonly List<string> _pendingAudio = new();

        private WeatherCondition  _weather = WeatherCondition.Clear;
        private WeatherCondition _previousWeather = WeatherCondition.Clear;
        private double _weatherTransitionRemainingMs = 0;
        private const double WeatherTransitionDurationMs = 60000; 

        public int RvrMeters { get; private set; } = 10000;

        private bool _isWindShearActive;
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

        private char _atisLetter = 'A';
        private string _currentAtis = string.Empty;

        private readonly LinkedList<SimSnapshot> _snapshotHistory = new();
        private const int MaxHistoryFrames = 600; 

        public SimulationEngine(IHubContext<SimulationHub, ISimulationClient> hubContext)
        {
            _hubContext       = hubContext;
            Clock             = new SimClock(DateTime.Today.AddHours(5));
            _scheduler        = new FlightScheduler();
            _runway           = new RunwayController();
            _gates            = new GateManager();
            _conflicts        = new ConflictDetector();
            _groundVehicles   = new GroundVehicleManager(); 
            _activeAircraft   = new List<Aircraft>();
        }

        // NEW: Toggle Ops Mode
        public void ToggleOpsMode(RunwayOpsMode mode)
        {
            _runway.SetOpsMode(mode);
        }

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

        public List<SimSnapshot> GetReplayBuffer()
        {
            return _snapshotHistory.ToList();
        }

        public WeatherCondition CycleWeather()
        {
            _previousWeather = _weather;
            int next = ((int)_weather + 1) % Enum.GetValues<WeatherCondition>().Length;
            _weather = (WeatherCondition)next;
            
            _weatherTransitionRemainingMs = WeatherTransitionDurationMs;
            
            RvrMeters = _weather switch {
                WeatherCondition.Clear => 10000,
                WeatherCondition.Cloudy => 8000,
                WeatherCondition.Rain => 3000,
                WeatherCondition.Storm => 800,
                WeatherCondition.Fog => 250, 
                _ => 10000
            };
            
            if (_weather == WeatherCondition.Storm || _weather == WeatherCondition.Rain)
            {
                _stormCenter = new SimPoint(-200, _rand.Next(100, 500));
                _stormVelocity = new SimPoint(40 + _rand.NextDouble() * 30, (_rand.NextDouble() - 0.5) * 20);
            }
            else
            {
                _stormCenter = null;
            }

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

        private void GenerateAtis()
        {
            string timeZ = Clock.SimulatedNow.ToString("HHmm");
            
            string wind = _weather switch {
                WeatherCondition.Clear => "VARIABLE AT 4",
                WeatherCondition.Cloudy => "270 AT 12",
                WeatherCondition.Rain => "240 AT 18",
                WeatherCondition.Storm => "210 AT 35 GUSTING 45",
                WeatherCondition.Fog => "CALM",
                _ => "000 AT 0"
            };

            string vis = RvrMeters >= 10000 ? "10 KILOMETERS OR MORE" : $"{RvrMeters} METERS";
            
            string clouds = _weather switch {
                WeatherCondition.Clear => "CAVOK",
                WeatherCondition.Cloudy => "BROKEN 3000 OVERCAST 8000",
                WeatherCondition.Rain => "SCATTERED 1500 OVERCAST 4000",
                WeatherCondition.Storm => "FEW 1000 CUMULONIMBUS BROKEN 2000",
                WeatherCondition.Fog => "VERTICAL VISIBILITY 100",
                _ => "CAVOK"
            };

            int qnh = 1013 + _rand.Next(-10, 10);

            _currentAtis = $"AIRPORT INFORMATION {_atisLetter}. {timeZ} ZULU. WIND {wind}. VISIBILITY {vis}. {clouds}. ALTIMETER {qnh}. ARRIVING RUNWAY 28L. DEPARTING RUNWAY 28R. ADVISE ON INITIAL CONTACT YOU HAVE INFORMATION {_atisLetter}.";

            _atisLetter++;
            if (_atisLetter > 'Z') _atisLetter = 'A';
        }

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

            if (_stormCenter.HasValue)
            {
                double moveSecs = simDeltaMs / 1000.0;
                _stormCenter = new SimPoint(
                    _stormCenter.Value.X + (_stormVelocity.X * moveSecs),
                    _stormCenter.Value.Y + (_stormVelocity.Y * moveSecs)
                );
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

            _groundVehicles.Tick(simDeltaMs);

            var holdingArrivals = _activeAircraft
                .Where(a => a.State.FlightType == FlightType.Arrival && a.State.Phase == AircraftPhase.Holding)
                .OrderByDescending(a => a.State.GoAroundCount)
                .ThenBy(a => a.State.FlightId)
                .ToList();

            var approachingCount = _activeAircraft.Count(a => a.State.FlightType == FlightType.Arrival && 
                (a.State.Phase == AircraftPhase.Approaching || a.State.Phase == AircraftPhase.OnFinal));

            if (approachingCount < 2 && holdingArrivals.Any())
            {
                var cleared = holdingArrivals.First();
                cleared.ClearFromHold();
                holdingArrivals.Remove(cleared);
                PushAlert($"📡 ATC: {cleared.State.FlightId} cleared from holding stack, resuming approach.");
                approachingCount++;
            }

            var next = _scheduler.PeekNextFlight();
            if (next != null && Clock.SimulatedNow >= next.ScheduledTime)
            {
                var flightEvent = _scheduler.DequeueNextFlight();
                
                // NEW: Dynamically assign runway based on Ops Mode
                RunwayId targetRunway = _runway.GetBestRunway(flightEvent.FlightType);
                var aircraft    = new Aircraft(flightEvent, targetRunway); 

                if (aircraft.State.FlightId.StartsWith("MAYDAY"))
                {
                    aircraft.DeclareEmergency("General Emergency");
                    PushAlert($"🚨 {aircraft.State.FlightId} on emergency approach for {targetRunway}");
                    _runway.DeclareEmergencyOverride(aircraft.State.FlightId, targetRunway);
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
                        if (approachingCount >= 2)
                        {
                            aircraft.SendToHold();
                            holdingArrivals.Add(aircraft);
                            PushAlert($"📡 ATC: Approach saturated. {aircraft.State.FlightId} routed to holding stack.");
                        }
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
                var turnaroundBefore = ac.State.Turnaround;
                var delayBefore = ac.State.DelayMinutes;
                
                int holdAlt = 0;
                if (ac.State.FlightType == FlightType.Arrival && ac.State.Phase == AircraftPhase.Holding)
                {
                    int stackIndex = holdingArrivals.IndexOf(ac);
                    holdAlt = 8000 + (stackIndex * 1000); 
                }

                ac.Tick(simDeltaMs, _runway, _gates, _weather, RvrMeters, holdAlt);

                if (ac.State.FlightType == FlightType.Departure && turnaroundBefore != ac.State.Turnaround)
                {
                    switch (ac.State.Turnaround)
                    {
                        case TurnaroundPhase.Cleaning: PushAlert($"🧹 {ac.State.FlightId} deplaning complete. Cleaning started."); break;
                        case TurnaroundPhase.Fueling: PushAlert($"⛽ {ac.State.FlightId} cleaning complete. Fueling started."); break;
                        case TurnaroundPhase.Boarding: PushAlert($"🚶 {ac.State.FlightId} fueling complete. Boarding started."); break;
                        case TurnaroundPhase.Ready: PushAlert($"✅ {ac.State.FlightId} turnaround complete. Ready for pushback."); break;
                    }
                }

                if (ac.State.FlightType == FlightType.Departure && delayBefore < ac.State.DelayMinutes)
                {
                    int addedDelay = ac.State.DelayMinutes - delayBefore;
                    PushAlert($"⏱ {ac.State.FlightId} delayed by {addedDelay} mins during {ac.State.Turnaround}!");
                }

                if (statusBefore != AircraftStatus.Emergency && ac.State.Status == AircraftStatus.Emergency)
                {
                    _pendingAudio.Add("alarm_emergency.wav");
                    PushAlert($"🚨 EMERGENCY DECLARED: {ac.State.FlightId} ({ac.State.EmergencyReason})");
                    _runway.DeclareEmergencyOverride(ac.State.FlightId, ac.State.AssignedRunway);
                }

                if (phaseBefore != ac.State.Phase)
                {
                    switch (ac.State.Phase)
                    {
                        case AircraftPhase.Approaching: _pendingAudio.Add("atc_cleared.wav"); break;
                        case AircraftPhase.GoAround: _pendingAudio.Add("atc_go_around.wav"); break;
                        case AircraftPhase.Taxiing: _pendingAudio.Add("engine_light.wav"); break;
                        case AircraftPhase.Takeoff: _pendingAudio.Add("engine_heavy.wav"); break;
                    }
                }

                if (statusBefore != AircraftStatus.Diverting && ac.State.Status == AircraftStatus.Diverting)
                {
                    PushAlert($"↩️ DIVERSION DECLARED: {ac.State.FlightId} proceeding to alternate. Reason: {ac.State.EmergencyReason}");
                }

                if (phaseBefore == AircraftPhase.Taxiing && ac.State.Phase == AircraftPhase.Parked && ac.State.FlightType == FlightType.Arrival)
                {
                    if (!AssignGate(ac, ac.State.AssignedGate)) PushAlert($"⚠ {ac.State.FlightId} parked but no gate free");
                    else PushAlert($"🅿 {ac.State.FlightId} parked at gate {ac.State.AssignedGate}");
                }

                if (phaseBefore == AircraftPhase.Parked && ac.State.Phase == AircraftPhase.Taxiing && ac.State.FlightType == FlightType.Departure)
                {
                    _gates.Release(ac.State.FlightId);
                    PushAlert($"🚪 {ac.State.FlightId} pushed back from {ac.State.AssignedGate}");
                }

                if (phaseBefore == AircraftPhase.OnFinal && ac.State.Phase == AircraftPhase.Holding) 
                {
                    _goAroundsToday++;
                    _conflicts.RecordGoAround(simNowMs);
                    
                    if (ac.LastGoAroundWasWeatherForced) PushAlert($"🌫️ {ac.State.FlightId} ({ac.State.Type}) aborted: Below approach minimums");
                    else PushAlert($"↩ {ac.State.FlightId} executing go-around into hold (attempt {ac.State.GoAroundCount})");
                }

                if (phaseBefore == AircraftPhase.Rollout && ac.State.Phase == AircraftPhase.Taxiing)
                {
                    _arrivalsToday++;
                    PushAlert($"✅ {ac.State.FlightId} landed and vacated");
                    
                    if (ac.State.Status == AircraftStatus.Emergency)
                    {
                        PushAlert($"🟢 {ac.State.FlightId} is safe on taxiway. Lifting airfield lockdown.");
                        _runway.SetEmergencyLockdown(false);
                    }
                }

                if (phaseBefore == AircraftPhase.Climbing && ac.State.Phase == AircraftPhase.Departed)
                {
                    _departuresToday++;
                    _gates.Release(ac.State.FlightId);
                    PushAlert($"✈ {ac.State.FlightId} departed");
                }

                if (ac.IsFinished)
                {
                    _gates.Release(ac.State.FlightId);
                    _activeAircraft.Remove(ac);
                    
                    _totalDelayMinutes += ac.State.DelayMinutes;
                    
                    if (ac.State.Phase == AircraftPhase.Diverted)
                    {
                        _diversionsToday++;
                        PushAlert($"🛬 {ac.State.FlightId} has successfully diverted out of local airspace.");
                    }
                }
            }

            var states = _activeAircraft.Select(a => a.State).ToList();
            _conflicts.Check(states, simNowMs, simDeltaMs);
            foreach (var alert in _conflicts.PendingAlerts)
                PushAlert(alert);
        }

        private string CalculateGrade()
        {
            int score = 100 
                - (_conflicts.TotalConflicts * 10) 
                - (_diversionsToday * 5) 
                - (_goAroundsToday * 2) 
                - (_totalDelayMinutes / 10);

            if (score >= 95) return "A+";
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
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
            var fids = new List<FidsEntry>();
            foreach (var ac in _activeAircraft.Where(a => a.State.FlightType == FlightType.Departure))
            {
                string status = ac.State.Turnaround == TurnaroundPhase.Ready ? "Ready" : ac.State.Turnaround.ToString();
                if (ac.State.Phase == AircraftPhase.Pushback) status = "Pushback";
                else if (ac.State.Phase == AircraftPhase.Taxiing) status = "Taxiing";
                else if (ac.State.Phase == AircraftPhase.Holding) status = "Holding";
                else if (ac.State.Phase == AircraftPhase.Takeoff) status = "Departing";
                else if (ac.State.Phase == AircraftPhase.Climbing) status = "Departed";
                else if (ac.State.DelayMinutes > 0) status = $"Delayed ({ac.State.DelayMinutes}m)";

                fids.Add(new FidsEntry {
                    FlightId = ac.State.FlightId,
                    Destination = ac.State.Destination,
                    Gate = ac.State.AssignedGate,
                    Status = status
                });
            }
            foreach (var q in _scheduler.GetQueuePreview(10).Where(f => f.FlightType == FlightType.Departure))
            {
                fids.Add(new FidsEntry {
                    FlightId = q.FlightId,
                    Destination = q.Destination,
                    Gate = q.Gate,
                    Status = q.DelayMinutes > 0 ? $"Delayed ({q.DelayMinutes}m)" : "Scheduled"
                });
            }

            var snapshot = new SimSnapshot
            {
                SimulatedTime       = Clock.SimulatedNow,
                TimeScale           = Clock.TimeScale,
                IsPaused            = Clock.IsPaused,
                Runways             = _runway.GetSnapshots(),
                ActiveAircraft      = _activeAircraft.Select(a => a.State).ToList(),
                GroundVehicles      = _groundVehicles.GetStates(), 
                QueuedFlights       = _scheduler.GetQueuePreview(5),
                DepartureBoard      = fids, 
                Weather             = _weather,
                PreviousWeather     = _previousWeather,
                WeatherTransitionProgress = 1.0 - (_weatherTransitionRemainingMs / WeatherTransitionDurationMs),
                IsWindShearActive   = _isWindShearActive,
                RvrMeters           = this.RvrMeters,
                StormCenter         = _stormCenter, 
                CurrentAtis         = _currentAtis, 
                RecentAlerts        = _alertLog.TakeLast(5).Reverse().ToList(),
                TotalArrivalsToday  = _arrivalsToday,
                TotalDeparturesToay = _departuresToday,
                GoAroundsToday      = _goAroundsToday,
                DiversionsToday     = _diversionsToday,
                ConflictCountToday  = _conflicts.TotalConflicts,
                TotalDelayMinutes   = _totalDelayMinutes,
                CurrentScoreGrade   = CalculateGrade()
            };

            _snapshotHistory.AddLast(snapshot);
            if (_snapshotHistory.Count > MaxHistoryFrames)
            {
                _snapshotHistory.RemoveFirst();
            }

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