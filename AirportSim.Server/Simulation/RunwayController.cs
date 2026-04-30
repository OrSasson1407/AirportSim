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

        // Weather and Emergency flags
        public bool IsClosedForWeather { get; private set; }
        
        // NEW: Halts all departures if an emergency is incoming
        public bool EmergencyLockdown { get; private set; } 

        // ── Public read access ────────────────────────────────────────────────

        public RunwayStatus ArrivalStatus   => _arrivalRunway.Status;
        public RunwayStatus DepartureStatus => _departureRunway.Status;

        public bool ArrivalFree   => _arrivalRunway.IsFree;
        public bool DepartureFree => _departureRunway.IsFree && !EmergencyLockdown;

        // Snapshot data for broadcast
        public List<RunwaySnapshot> GetSnapshots() => new()
        {
            _arrivalRunway.ToSnapshot(),
            _departureRunway.ToSnapshot()
        };

        // ── Environmental & Emergency Control ─────────────────────────────────

        public void SetWeatherClosure(bool isClosed)
        {
            IsClosedForWeather = isClosed;
            if (isClosed)
            {
                PendingAlerts.Add("⛈ ALERT: Runways closed due to severe weather minimums.");
            }
        }

        public void SetEmergencyLockdown(bool isLocked)
        {
            EmergencyLockdown = isLocked;
            if (isLocked)
            {
                PendingAlerts.Add("🚨 AIRFIELD LOCKDOWN: All departures holding for emergency arrival.");
            }
            else
            {
                PendingAlerts.Add("✅ LOCKDOWN LIFTED: Normal departure operations resuming.");
            }
        }

        // ── Arrival runway ────────────────────────────────────────────────────

        public bool TryOccupyArrival(string flightId)
        {
            if (IsClosedForWeather) return false;
            return _arrivalRunway.TryOccupy(flightId);
        }

        public void ReleaseArrival(string flightId)
            => _arrivalRunway.Release(flightId);

        // ── Departure runway ──────────────────────────────────────────────────

        public bool TryOccupyDeparture(string flightId)
        {
            if (IsClosedForWeather || EmergencyLockdown) return false;
            return _departureRunway.TryOccupy(flightId);
        }

        public void ReleaseDeparture(string flightId)
            => _departureRunway.Release(flightId);

        // ── Emergency override: clears arrival runway immediately ─────────────

        public void DeclareEmergencyOverride(string flightId)
        {
            if (!_arrivalRunway.IsFree && _arrivalRunway.OccupantId != flightId)
            {
                PendingAlerts.Add(
                    $"🚨 EMERGENCY OVERRIDE: {flightId} forcing go-around for {_arrivalRunway.OccupantId} on 28L!");
                _arrivalRunway.ForceRelease();
            }
            
            SetEmergencyLockdown(true);
        }

        // ── Safety tick: detect stuck aircraft and process cooldowns ──────────

        public void Tick(double simDeltaMs)
        {
            TickSlot(_arrivalRunway,   simDeltaMs, "28L");
            TickSlot(_departureRunway, simDeltaMs, "28R");
        }

        private void TickSlot(RunwaySlot slot, double simDeltaMs, string name)
        {
            // Process wake turbulence/separation cooldown
            if (slot.CooldownMs > 0)
            {
                slot.CooldownMs -= simDeltaMs;
                if (slot.CooldownMs < 0) slot.CooldownMs = 0;
            }

            // Process occupied timeout
            if (slot.Status == RunwayStatus.Occupied)
            {
                slot.OccupiedForSimMs += simDeltaMs;

                if (slot.OccupiedForSimMs > 8 * 60_000) // 8 virtual minutes
                {
                    PendingAlerts.Add(
                        $"⚠ INCURSION: Runway {name} timeout — forcing clear (was: {slot.OccupantId})");
                    slot.ForceRelease();
                }
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
            
            // NEW: Separation timer
            public double       CooldownMs    { get; set; }

            // Runway is only free if status is Free AND the wake turbulence cooldown is finished
            public bool IsFree => Status == RunwayStatus.Free && CooldownMs <= 0;

            public RunwaySlot(RunwayId id, string name) { Id = id; Name = name; }

            public bool TryOccupy(string flightId)
            {
                if (!IsFree) return false;
                Status           = RunwayStatus.Occupied;
                OccupantId       = flightId;
                OccupiedForSimMs = 0;
                CooldownMs       = 0;
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
                // Enforce 1.5 virtual minutes of separation (wake turbulence) before next aircraft can enter
                CooldownMs       = 1.5 * 60_000; 
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