using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public class FlightScheduler
    {
        private readonly Queue<FlightEvent> _scheduledQueue = new();
        private readonly Random _rand = new();
        private DateTime _lastScheduledTime;

        public List<FlightEvent> GetQueuePreview(int count = 5)
        {
            return _scheduledQueue.Take(count).ToList();
        }

        public void Initialize(DateTime simStartTime)
        {
            _lastScheduledTime = simStartTime;
            RefillQueue(simStartTime, TimeSpan.FromMinutes(30));
        }

        public void Update(DateTime simNow)
        {
            if ((_lastScheduledTime - simNow).TotalMinutes < 10)
            {
                RefillQueue(_lastScheduledTime, TimeSpan.FromMinutes(20)); 
            }
        }

        public FlightEvent? PeekNextFlight()
        {
            return _scheduledQueue.Count > 0 ? _scheduledQueue.Peek() : null;
        }

        public FlightEvent DequeueNextFlight()
        {
            return _scheduledQueue.Dequeue();
        }

        private void RefillQueue(DateTime fromTime, TimeSpan durationToFill)
        {
            DateTime targetTime = fromTime.Add(durationToFill);
            AircraftType lastType = AircraftType.Small; 

            while (_lastScheduledTime < targetTime)
            {
                var flight = new FlightEvent
                {
                    FlightId = GenerateFlightId(),
                    Type = (AircraftType)_rand.Next(0, 3),      
                    FlightType = (FlightType)_rand.Next(0, 2)   
                };

                int separationMins = GetSeparationMinutes(lastType, flight.Type);
                _lastScheduledTime = _lastScheduledTime.AddMinutes(separationMins);
                
                // Add the exact time this flight is allowed to spawn
                flight.ScheduledTime = _lastScheduledTime; 
                
                _scheduledQueue.Enqueue(flight);
                lastType = flight.Type;
            }
        }

        private int GetSeparationMinutes(AircraftType preceding, AircraftType following)
        {
            return preceding switch
            {
                AircraftType.Small => following switch
                {
                    AircraftType.Large => 2,
                    _ => 1
                },
                AircraftType.Medium => following switch
                {
                    AircraftType.Large => 3,
                    _ => 2
                },
                AircraftType.Large => following switch
                {
                    AircraftType.Large => 4,
                    _ => 3
                },
                _ => 1
            };
        }

        private string GenerateFlightId()
        {
            string[] airlines = { "EL-AL", "ARK", "ISR", "BA", "AF", "LH", "UA" };
            return $"{airlines[_rand.Next(airlines.Length)]} {_rand.Next(100, 999)}";
        }
    }
}