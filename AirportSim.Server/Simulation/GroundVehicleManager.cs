using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public class GroundVehicle
    {
        public GroundVehicleState State { get; set; }
        private readonly List<SimPoint> _route;
        private int _currentWaypoint = 0;
        private readonly double _speedWuPerMs; 

        public GroundVehicle(string id, GroundVehicleType type, List<SimPoint> route, double speedKts)
        {
            State = new GroundVehicleState { Id = id, Type = type, Position = route[0], Heading = 0 };
            _route = route;
            // Converting theoretical Kts to WorldUnits/Ms for the simulation grid
            _speedWuPerMs = (speedKts * 0.005);
        }

        public void Tick(double simDeltaMs)
        {
            if (_route == null || _route.Count < 2) return;

            var currentPos = State.Position;
            var targetPos = _route[(_currentWaypoint + 1) % _route.Count];

            double dx = targetPos.X - currentPos.X;
            double dy = targetPos.Y - currentPos.Y;
            double distToTarget = Math.Sqrt(dx * dx + dy * dy);
            
            double moveDist = _speedWuPerMs * simDeltaMs;

            if (moveDist >= distToTarget)
            {
                // Reached the waypoint, cycle to the next one
                State.Position = targetPos;
                _currentWaypoint = (_currentWaypoint + 1) % _route.Count;
            }
            else
            {
                // Interpolate towards the target waypoint
                double ratio = moveDist / distToTarget;
                State.Position = new SimPoint(currentPos.X + dx * ratio, currentPos.Y + dy * ratio);
                
                // Keep the heading updated
                double radians = Math.Atan2(dy, dx);
                double degrees = radians * (180.0 / Math.PI);
                State.Heading = (degrees + 90 + 360) % 360;
            }
        }
    }

    public class GroundVehicleManager
    {
        private readonly List<GroundVehicle> _vehicles = new();

        public void Initialize(GateManager gates)
        {
            _vehicles.Clear();
            int idCounter = 1;

            foreach (var kvp in gates.GroundRoutes)
            {
                string routeName = kvp.Key;
                var path = kvp.Value;
                if (path.Count < 2) continue;

                GroundVehicleType type = GroundVehicleType.BaggageCart;
                double speed = 15; // default speed

                // Identify vehicle type based on JSON route naming convention
                if (routeName.Contains("Fuel", StringComparison.OrdinalIgnoreCase))
                {
                    type = GroundVehicleType.FuelTruck;
                    speed = 12;
                }
                else if (routeName.Contains("Emergency", StringComparison.OrdinalIgnoreCase))
                {
                    type = GroundVehicleType.FireEngine;
                    speed = 35; // Emergency vehicles move faster
                }

                // Spawn a vehicle on this loop
                _vehicles.Add(new GroundVehicle($"GV{idCounter++}", type, path, speed));
            }
        }

        public void Tick(double simDeltaMs)
        {
            foreach (var v in _vehicles)
            {
                v.Tick(simDeltaMs);
            }
        }

        public List<GroundVehicleState> GetStates()
        {
            return _vehicles.Select(v => v.State).ToList();
        }
    }
}