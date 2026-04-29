using System.Collections.Generic;
using System.Linq;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    /// <summary>
    /// Tracks which gates are occupied and assigns stands to new aircraft.
    /// Gates A1-A4  →  X 160–460,  Y 400  (left apron)
    /// Gates B1-B4  →  X 560–860,  Y 400  (centre apron)
    /// Gates C1-C4  →  X 960–1260, Y 400  (right apron)
    /// </summary>
    public class GateManager
    {
        private class GateSlot
        {
            public string Name      { get; }
            public double X         { get; }
            public double Y         { get; }
            public string? Occupant { get; set; }   // FlightId or null

            public bool IsFree => Occupant == null;

            public GateSlot(string name, double x, double y)
            {
                Name = name; X = x; Y = y;
            }
        }

        private readonly List<GateSlot> _gates = new()
        {
            // Terminal A — left
            new("A1",  160, 400), new("A2",  220, 400),
            new("A2",  280, 400), new("A4",  340, 400),

            // Terminal B — centre
            new("B1",  560, 400), new("B2",  620, 400),
            new("B3",  680, 400), new("B4",  740, 400),

            // Terminal C — right
            new("C1",  960, 400), new("C2", 1020, 400),
            new("C3", 1080, 400), new("C4", 1140, 400),
        };

        // Gates requested by the scheduler (from FlightEvent.Gate) get
        // priority; if that gate is taken we fall back to any free slot.
        public (string gateName, double gateX, double gateY)? Assign(
            string flightId, string preferredGate)
        {
            // Try preferred gate first
            var preferred = _gates.FirstOrDefault(
                g => g.Name == preferredGate && g.IsFree);

            if (preferred != null)
            {
                preferred.Occupant = flightId;
                return (preferred.Name, preferred.X, preferred.Y);
            }

            // Fall back to any free gate
            var fallback = _gates.FirstOrDefault(g => g.IsFree);
            if (fallback != null)
            {
                fallback.Occupant = flightId;
                return (fallback.Name, fallback.X, fallback.Y);
            }

            // Airport full — caller must queue the aircraft
            return null;
        }

        public void Release(string flightId)
        {
            var slot = _gates.FirstOrDefault(g => g.Occupant == flightId);
            if (slot != null) slot.Occupant = null;
        }

        public bool HasFreeGate() => _gates.Any(g => g.IsFree);

        // Snapshot list for the renderer (gate name → world position)
        public IReadOnlyList<(string name, double x, double y, string? occupant)>
            GetSnapshot() =>
            _gates.Select(g => (g.Name, g.X, g.Y, g.Occupant)).ToList();
    }
}