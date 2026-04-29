using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public class FlightScheduler
    {
        private readonly Queue<FlightEvent> _scheduledQueue = new();
        private readonly Random             _rand           = new();
        private DateTime                    _lastScheduledTime;

        // NEW: expanded airline list with ICAO-style codes
        private static readonly string[] Airlines =
        {
            "EL-AL", "ARK", "ISR", "BA", "AF", "LH", "UA",
            "TK", "QR", "EK", "AA", "DL", "IB", "KL", "SU"
        };

        // NEW: pool of airport codes for origin/destination display
        private static readonly string[] Airports =
        {
            "TLV", "JFK", "LHR", "CDG", "FRA", "DXB", "IST",
            "AMS", "MAD", "FCO", "ATH", "VIE", "ZRH", "CPH"
        };

        // NEW: gate naming pool
        private static readonly string[] Gates =
        {
            "A1","A2","A3","A4","B1","B2","B3","B4",
            "C1","C2","C3","C12","D7","D8"
        };

        public List<FlightEvent> GetQueuePreview(int count = 5) =>
            _scheduledQueue.Take(count).ToList();

        public void Initialize(DateTime simStartTime)
        {
            _lastScheduledTime = simStartTime;
            RefillQueue(simStartTime, TimeSpan.FromMinutes(30));
        }

        public void Update(DateTime simNow)
        {
            // Refill when the lookahead buffer drops below 10 sim-minutes
            if ((_lastScheduledTime - simNow).TotalMinutes < 10)
                RefillQueue(_lastScheduledTime, TimeSpan.FromMinutes(20));
        }

        public FlightEvent? PeekNextFlight()  =>
            _scheduledQueue.Count > 0 ? _scheduledQueue.Peek() : null;

        public FlightEvent DequeueNextFlight() =>
            _scheduledQueue.Dequeue();

        // NEW: force-inject an emergency flight at the front of the queue
        public void InjectEmergency(AircraftType type, FlightType flightType)
        {
            var emergency = new FlightEvent
            {
                FlightId      = $"MAYDAY {_rand.Next(10, 99)}",
                Type          = type,
                FlightType    = flightType,
                ScheduledTime = DateTime.MinValue,   // spawn immediately
                Origin        = Airports[_rand.Next(Airports.Length)],
                Destination   = "TLV",
                Gate          = Gates[_rand.Next(Gates.Length)],
                DelayMinutes  = 0
            };

            // Prepend by rebuilding — Queue<T> has no prepend
            var temp = new List<FlightEvent> { emergency };
            temp.AddRange(_scheduledQueue);
            _scheduledQueue.Clear();
            foreach (var f in temp) _scheduledQueue.Enqueue(f);
        }

        private void RefillQueue(DateTime fromTime, TimeSpan duration)
        {
            DateTime    target   = fromTime.Add(duration);
            AircraftType lastType = AircraftType.Small;

            while (_lastScheduledTime < target)
            {
                var type       = (AircraftType)_rand.Next(0, 3);
                var flightType = (FlightType)_rand.Next(0, 2);
                var airline    = Airlines[_rand.Next(Airlines.Length)];
                var origin     = Airports[_rand.Next(Airports.Length)];
                string dest    = flightType == FlightType.Arrival ? "TLV" : Airports[_rand.Next(Airports.Length)];

                // Avoid same origin == destination
                if (dest == origin) dest = "JFK";

                var flight = new FlightEvent
                {
                    FlightId      = $"{airline} {_rand.Next(100, 999)}",
                    Type          = type,
                    FlightType    = flightType,
                    ScheduledTime = _lastScheduledTime,
                    Origin        = origin,
                    Destination   = dest,
                    Gate          = Gates[_rand.Next(Gates.Length)],
                    DelayMinutes  = _rand.Next(0, 4) == 0 ? _rand.Next(5, 45) : 0  // 25% chance of delay
                };

                int sep = GetSeparationMinutes(lastType, type);
                _lastScheduledTime = _lastScheduledTime.AddMinutes(sep);
                flight.ScheduledTime = _lastScheduledTime;

                _scheduledQueue.Enqueue(flight);
                lastType = type;
            }
        }

        private int GetSeparationMinutes(AircraftType preceding, AircraftType following) =>
            preceding switch
            {
                AircraftType.Small  => following == AircraftType.Large ? 2 : 1,
                AircraftType.Medium => following == AircraftType.Large ? 3 : 2,
                AircraftType.Large  => following == AircraftType.Large ? 4 : 3,
                _                   => 1
            };
    }
}