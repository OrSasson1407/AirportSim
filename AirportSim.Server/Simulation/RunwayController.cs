using System;
using System.Collections.Generic;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public class RunwayController
    {
        public RunwayStatus Status { get; private set; } = RunwayStatus.Free;

        private string?  _currentOccupantId;
        private DateTime _occupiedSince;

        // NEW: tracks how long the runway has been occupied (sim ms)
        public double OccupiedForSimMs { get; private set; }

        // NEW: alert log written to by the controller; SimulationEngine reads and clears it
        public readonly List<string> PendingAlerts = new();

        public bool IsFree => Status == RunwayStatus.Free;

        public bool TryOccupy(string flightId)
        {
            if (!IsFree) return false;

            Status              = RunwayStatus.Occupied;
            _currentOccupantId  = flightId;
            _occupiedSince      = DateTime.UtcNow;
            OccupiedForSimMs    = 0;
            return true;
        }

        public void Release(string flightId)
        {
            if (_currentOccupantId != flightId) return;

            Status             = RunwayStatus.Free;
            _currentOccupantId = null;
            OccupiedForSimMs   = 0;
        }

        // NEW: called each tick so the controller can detect runway incursions / stuck aircraft
        public void Tick(double simDeltaMs)
        {
            if (Status == RunwayStatus.Occupied)
            {
                OccupiedForSimMs += simDeltaMs;

                // Safety valve: if a plane has been on the runway for more than
                // 8 simulated minutes something went wrong — force a release
                if (OccupiedForSimMs > 8 * 60_000)
                {
                    PendingAlerts.Add($"⚠ Runway incursion timeout — forcing clear (was: {_currentOccupantId})");
                    Status             = RunwayStatus.Free;
                    _currentOccupantId = null;
                    OccupiedForSimMs   = 0;
                }
            }
        }

        // NEW: emergency aircraft may jump the queue and clear the runway immediately
        public void DeclareEmergencyOverride(string flightId)
        {
            if (_currentOccupantId != null && _currentOccupantId != flightId)
                PendingAlerts.Add($"🚨 Emergency override: {flightId} cleared runway from {_currentOccupantId}");

            Status             = RunwayStatus.Free;
            _currentOccupantId = null;
            OccupiedForSimMs   = 0;
        }
    }
}