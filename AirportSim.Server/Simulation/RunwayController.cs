using System.Collections.Generic;
using System.Linq;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    public enum RunwayOpsMode { Segregated, Mixed }

    public class RunwayController
    {
        private readonly List<RunwaySlot> _runways = new()
        {
            new RunwaySlot(RunwayId.Runway28L, "28L"),
            new RunwaySlot(RunwayId.Runway28R, "28R")
        };

        public readonly List<string> PendingAlerts = new();

        public bool IsClosedForWeather { get; private set; }
        public bool EmergencyLockdown { get; private set; } 

        // NEW: Operations Mode
        public RunwayOpsMode OpsMode { get; private set; } = RunwayOpsMode.Segregated;

        public void SetOpsMode(RunwayOpsMode mode)
        {
            OpsMode = mode;
            string modeStr = mode == RunwayOpsMode.Segregated ? "SEGREGATED (28L Arr / 28R Dep)" : "MIXED (Both Runways)";
            PendingAlerts.Add($"🔄 Runway operations changed to: {modeStr}");
        }

        public List<RunwaySnapshot> GetSnapshots() => _runways.Select(r => r.ToSnapshot()).ToList();

        public void SetWeatherClosure(bool isClosed)
        {
            IsClosedForWeather = isClosed;
            if (isClosed) PendingAlerts.Add("⛈ ALERT: Runways closed due to severe weather minimums.");
        }

        public void SetEmergencyLockdown(bool isLocked)
        {
            EmergencyLockdown = isLocked;
            if (isLocked) PendingAlerts.Add("🚨 AIRFIELD LOCKDOWN: All departures holding for emergency arrival.");
            else PendingAlerts.Add("✅ LOCKDOWN LIFTED: Normal departure operations resuming.");
        }

        // NEW: Assign best runway based on Ops Mode
        public RunwayId GetBestRunway(FlightType flightType)
        {
            if (OpsMode == RunwayOpsMode.Segregated)
            {
                return flightType == FlightType.Arrival ? RunwayId.Runway28L : RunwayId.Runway28R;
            }
            else
            {
                // In Mixed mode, pick the runway with the lowest cooldown/occupancy
                var bestRunway = _runways.OrderBy(r => r.CooldownMs).ThenBy(r => r.IsFree ? 0 : 1).First();
                return bestRunway.Id;
            }
        }

        public bool TryOccupy(RunwayId id, string flightId, FlightType type)
        {
            if (IsClosedForWeather) return false;
            if (type == FlightType.Departure && EmergencyLockdown) return false;

            var runway = _runways.FirstOrDefault(r => r.Id == id);
            return runway != null && runway.TryOccupy(flightId);
        }

        public void Release(RunwayId id, string flightId)
        {
            var runway = _runways.FirstOrDefault(r => r.Id == id);
            runway?.Release(flightId);
        }

        public void DeclareEmergencyOverride(string flightId, RunwayId assignedRunway)
        {
            var runway = _runways.FirstOrDefault(r => r.Id == assignedRunway);
            if (runway != null && !runway.IsFree && runway.OccupantId != flightId)
            {
                PendingAlerts.Add($"🚨 EMERGENCY OVERRIDE: {flightId} forcing go-around for {runway.OccupantId} on {runway.Name}!");
                runway.ForceRelease();
            }
            
            SetEmergencyLockdown(true);
        }

        public void Tick(double simDeltaMs)
        {
            foreach (var slot in _runways)
            {
                if (slot.CooldownMs > 0)
                {
                    slot.CooldownMs -= simDeltaMs;
                    if (slot.CooldownMs < 0) slot.CooldownMs = 0;
                }

                if (slot.Status == RunwayStatus.Occupied)
                {
                    slot.OccupiedForSimMs += simDeltaMs;
                    if (slot.OccupiedForSimMs > 8 * 60_000) 
                    {
                        PendingAlerts.Add($"⚠ INCURSION: Runway {slot.Name} timeout — forcing clear (was: {slot.OccupantId})");
                        slot.ForceRelease();
                    }
                }
            }
        }

        private class RunwaySlot
        {
            public RunwayId     Id            { get; }
            public string       Name          { get; }
            public RunwayStatus Status        { get; private set; } = RunwayStatus.Free;
            public string?      OccupantId    { get; private set; }
            public double       OccupiedForSimMs { get; set; }
            public double       CooldownMs    { get; set; }

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