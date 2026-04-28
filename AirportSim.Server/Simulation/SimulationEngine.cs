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
        private readonly SimClock _clock;
        private readonly FlightScheduler _scheduler;
        private readonly RunwayController _runway;
        private readonly List<Aircraft> _activeAircraft;

        private const int TickIntervalMs = 100;
        private const int BroadcastIntervalMs = 200;

        public SimulationEngine(IHubContext<SimulationHub, ISimulationClient> hubContext)
        {
            _hubContext = hubContext;
            _clock = new SimClock(DateTime.Today.AddHours(5)); // Start at 05:00 AM (Dawn)
            _scheduler = new FlightScheduler();
            _runway = new RunwayController();
            _activeAircraft = new List<Aircraft>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _scheduler.Initialize(_clock.SimulatedNow);
            DateTime lastBroadcast = DateTime.UtcNow;
            DateTime lastTick = DateTime.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime now = DateTime.UtcNow;
                int elapsedMs = (int)(now - lastTick).TotalMilliseconds;

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

                // Yield to prevent pegging the CPU
                await Task.Delay(10, stoppingToken);
            }
        }

        private void UpdateSimulation(int elapsedMs)
        {
            _clock.Tick(elapsedMs);
            _scheduler.Update(_clock.SimulatedNow);

            // Spawn new flights from the queue
            var nextFlight = _scheduler.PeekNextFlight();
            // <-- Update this if statement to check the time!
            if (nextFlight != null && _clock.SimulatedNow >= nextFlight.ScheduledTime) 
            {
                // We bring planes into the active sim roughly 10 minutes before their scheduled time
                // Or immediately if they are departures parked at the gate
                _activeAircraft.Add(new Aircraft(_scheduler.DequeueNextFlight()));
            }

            // Tick all active aircraft
            double simDeltaMs = elapsedMs * _clock.TimeScale;
            foreach (var aircraft in _activeAircraft.ToList())
            {
                aircraft.Tick(simDeltaMs, _runway);

                // Remove departed or parked aircraft that have completed their cycle
                if (aircraft.State.Phase == AircraftPhase.Departed || 
                    (aircraft.State.FlightType == FlightType.Arrival && aircraft.State.Phase == AircraftPhase.Parked && aircraft.State.PhaseProgress >= 1.0))
                {
                    _activeAircraft.Remove(aircraft);
                }
            }
        }

        private async Task BroadcastSnapshot()
        {
            var snapshot = new SimSnapshot
            {
                SimulatedTime = _clock.SimulatedNow,
                TimeScale = _clock.TimeScale,
                RunwayStatus = _runway.Status,
                ActiveAircraft = _activeAircraft.Select(a => a.State).ToList(),
                QueuedFlights = _scheduler.GetQueuePreview(5)
            };

            await _hubContext.Clients.All.ReceiveSnapshot(snapshot);
        }
    }
}