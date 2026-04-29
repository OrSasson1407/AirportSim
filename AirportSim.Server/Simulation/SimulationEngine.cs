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

        // NEW: public so SimulationHub can read/write them directly
        public  SimClock          Clock     { get; }
        private FlightScheduler   _scheduler;
        private RunwayController  _runway;
        private List<Aircraft>    _activeAircraft;

        // NEW: weather state + rolling alert log
        private WeatherCondition  _weather = WeatherCondition.Clear;
        private readonly List<string> _alertLog = new();
        private const int AlertLogMax = 20;

        // NEW: daily counters
        private int _arrivalsToday;
        private int _departuresToday;
        private int _goAroundsToday;

        private const int TickIntervalMs      = 100;
        private const int BroadcastIntervalMs = 200;

        // NEW: weather cycles every ~5 real minutes automatically
        private DateTime _lastWeatherChange = DateTime.UtcNow;
        private const int WeatherChangeSec  = 300;

        public SimulationEngine(IHubContext<SimulationHub, ISimulationClient> hubContext)
        {
            _hubContext     = hubContext;
            Clock           = new SimClock(DateTime.Today.AddHours(5)); // start at 05:00 dawn
            _scheduler      = new FlightScheduler();
            _runway         = new RunwayController();
            _activeAircraft = new List<Aircraft>();
        }

        // ── Public API called by SimulationHub ────────────────────────────────

        public void InjectEmergency()
        {
            _scheduler.InjectEmergency(AircraftType.Medium, FlightType.Arrival);
            PushAlert("🚨 MAYDAY — emergency aircraft added to queue");
        }

        public WeatherCondition CycleWeather()
        {
            int next = ((int)_weather + 1) % Enum.GetValues<WeatherCondition>().Length;
            _weather = (WeatherCondition)next;
            ApplyWeatherEffects();
            PushAlert($"🌤 Weather → {_weather}");
            return _weather;
        }

        // ── Background loop ───────────────────────────────────────────────────

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

                // Auto-cycle weather every WeatherChangeSec real seconds
                if ((now - _lastWeatherChange).TotalSeconds >= WeatherChangeSec)
                {
                    CycleWeather();
                    _lastWeatherChange = now;
                }

                await Task.Delay(10, stoppingToken);
            }
        }

        // ── Simulation tick ───────────────────────────────────────────────────

        private void UpdateSimulation(int realElapsedMs)
        {
            Clock.Tick(realElapsedMs);
            _scheduler.Update(Clock.SimulatedNow);

            // Spawn flights whose scheduled time has arrived
            var next = _scheduler.PeekNextFlight();
            if (next != null && Clock.SimulatedNow >= next.ScheduledTime)
            {
                var aircraft = new Aircraft(_scheduler.DequeueNextFlight());

                // NEW: mark emergency aircraft and apply weather-driven go-around chance
                if (aircraft.State.FlightId.StartsWith("MAYDAY"))
                {
                    aircraft.DeclareEmergency();
                    PushAlert($"🚨 {aircraft.State.FlightId} on emergency approach");
                }
                else
                {
                    aircraft.GoAroundChance = WeatherGoAroundChance(_weather);
                }

                _activeAircraft.Add(aircraft);
            }

            // Tick the runway safety timer
            double simDeltaMs = realElapsedMs * Clock.TimeScale;
            _runway.Tick(simDeltaMs);

            // Drain runway alerts
            foreach (var alert in _runway.PendingAlerts) PushAlert(alert);
            _runway.PendingAlerts.Clear();

            // Tick all aircraft
            foreach (var ac in _activeAircraft.ToList())
            {
                var phaseBefore = ac.State.Phase;
                ac.Tick(simDeltaMs, _runway);

                // Detect go-arounds
                if (phaseBefore == AircraftPhase.OnFinal && ac.State.Phase == AircraftPhase.GoAround)
                {
                    _goAroundsToday++;
                    PushAlert($"↩ {ac.State.FlightId} executing go-around (attempt {ac.State.GoAroundCount})");
                }

                // Detect landings completed
                if (phaseBefore == AircraftPhase.Rollout && ac.State.Phase == AircraftPhase.Taxiing)
                {
                    _arrivalsToday++;
                    PushAlert($"✅ {ac.State.FlightId} landed and vacated runway");
                }

                // Detect departures
                if (phaseBefore == AircraftPhase.Climbing && ac.State.Phase == AircraftPhase.Departed)
                {
                    _departuresToday++;
                    PushAlert($"✈ {ac.State.FlightId} departed");
                }

                // Remove finished aircraft
                if (ac.IsFinished)
                    _activeAircraft.Remove(ac);
            }
        }

        // ── Broadcast ─────────────────────────────────────────────────────────

        private async Task BroadcastSnapshot()
        {
            var snapshot = new SimSnapshot
            {
                SimulatedTime      = Clock.SimulatedNow,
                TimeScale          = Clock.TimeScale,
                IsPaused           = Clock.IsPaused,
                RunwayStatus       = _runway.Status,
                ActiveAircraft     = _activeAircraft.Select(a => a.State).ToList(),
                QueuedFlights      = _scheduler.GetQueuePreview(5),
                Weather            = _weather,
                RecentAlerts       = _alertLog.TakeLast(5).Reverse().ToList(),
                TotalArrivalsToday  = _arrivalsToday,
                TotalDeparturesToay = _departuresToday,
                GoAroundsToday      = _goAroundsToday
            };

            await _hubContext.Clients.All.ReceiveSnapshot(snapshot);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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