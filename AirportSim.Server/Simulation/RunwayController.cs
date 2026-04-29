using System.Collections.Generic;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    /// <summary>
    /// Manages both physical runways independently.
    /// Runway 28L  →  arrivals only.
    /// Runway 28R  →  departures only (with emergency override).
    /// </summary>
    public class RunwayController
    {
        // ── Per-runway state ──────────────────────────────────────────────────

        private readonly RunwaySlot _arrivalRunway   = new(RunwayId.Runway28L, "28L ARR");
        private readonly RunwaySlot _departureRunway = new(RunwayId.Runway28R, "28R DEP");

        public readonly List<string> PendingAlerts = new();

        // NEW: Weather closure flag
        public bool IsClosedForWeather { get; private set; }

        // ── Public read access ────────────────────────────────────────────────

        public RunwayStatus ArrivalStatus   => _arrivalRunway.Status;
        public RunwayStatus DepartureStatus => _departureRunway.Status;

        public bool ArrivalFree   => _arrivalRunway.IsFree;
        public bool DepartureFree => _departureRunway.IsFree;

        // Snapshot data for broadcast
        public List<RunwaySnapshot> GetSnapshots() => new()
        {
            _arrivalRunway.ToSnapshot(),
            _departureRunway.ToSnapshot()
        };

        // ── Weather Control ───────────────────────────────────────────────────

        public void SetWeatherClosure(bool isClosed)
        {
            IsClosedForWeather = isClosed;
        }

        // ── Arrival runway ────────────────────────────────────────────────────

        public bool TryOccupyArrival(string flightId)
        {
            // Reject clearances if runway is closed for weather
            if (IsClosedForWeather) return false;
            return _arrivalRunway.TryOccupy(flightId);
        }

        public void ReleaseArrival(string flightId)
            => _arrivalRunway.Release(flightId);

        // ── Departure runway ──────────────────────────────────────────────────

        public bool TryOccupyDeparture(string flightId)
        {
            // Reject clearances if runway is closed for weather
            if (IsClosedForWeather) return false;
            return _departureRunway.TryOccupy(flightId);
        }

        public void ReleaseDeparture(string flightId)
            => _departureRunway.Release(flightId);

        // ── Emergency override: clears arrival runway immediately ─────────────

        public void DeclareEmergencyOverride(string flightId)
        {
            if (!_arrivalRunway.IsFree)
            {
                PendingAlerts.Add(
                    $"🚨 Emergency override: {flightId} cleared 28L from {_arrivalRunway.OccupantId}");
                _arrivalRunway.ForceRelease();
            }
        }

        // ── Safety tick: detect stuck aircraft on either runway ───────────────

        public void Tick(double simDeltaMs)
        {
            TickSlot(_arrivalRunway,   simDeltaMs, "28L");
            TickSlot(_departureRunway, simDeltaMs, "28R");
        }

        private void TickSlot(RunwaySlot slot, double simDeltaMs, string name)
        {
            if (slot.Status != RunwayStatus.Occupied) return;

            slot.OccupiedForSimMs += simDeltaMs;

            if (slot.OccupiedForSimMs > 8 * 60_000)
            {
                PendingAlerts.Add(
                    $"⚠ Runway {name} incursion timeout — forcing clear (was: {slot.OccupantId})");
                slot.ForceRelease();
            }
        }

        // ── Inner slot class ──────────────────────────────────────────────────

        private class RunwaySlot
        {
            public RunwayId     Id            { get; }
            public string       Name          { get; }
            public RunwayStatus Status        { get; private set; } = RunwayStatus.Free;
            public string?      OccupantId    { get; private set; }
            public double       OccupiedForSimMs { get; set; }

            public bool IsFree => Status == RunwayStatus.Free;

            public RunwaySlot(RunwayId id, string name) { Id = id; Name = name; }

            public bool TryOccupy(string flightId)
            {
                if (!IsFree) return false;
                Status           = RunwayStatus.Occupied;
                OccupantId       = flightId;
                OccupiedForSimMs = 0;
                return true;
            }

            public void Release(string flightId)
            {
                if (OccupantId != flightId) return;
                ForceRelease();
            }

            public void ForceRelease()
            {
                Status           = RunwayStatus.Free;
                OccupantId       = null;
                OccupiedForSimMs = 0;
            }

            public RunwaySnapshot ToSnapshot() => new()
            {
                Id         = Id,
                Name       = Name,
                Status     = Status,
                OccupiedBy = OccupantId
            };
        }
    }
}