using System.Collections.Generic;
using System.Linq;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    /// <summary>
    /// Tracks which gates are occupied and assigns stands to new aircraft.
    ///
    /// Gate layout — derived from airport-layout-tlv.json (all Y = 400):
    ///
    ///   Terminal A  (Domestic & Charter)   X 160 – 460    Gates A1–A6
    ///   Terminal B  (International)        X 560 – 860    Gates B1–B6
    ///   Terminal C  (Long Haul)            X 980 – 1280   Gates C1, C2, C3, C4, C12, C14
    ///   Terminal D  (El Al Hub)            X 1510 – 1810  Gates D1, D2, D7–D10
    ///
    /// Size matching:
    ///   Small  aircraft  → prefers Terminal A gates (A1–A6)
    ///   Medium aircraft  → prefers Terminal B or C gates
    ///   Large  aircraft  → prefers Terminal C or D gates
    /// </summary>
    public class GateManager
    {
        // ── Gate size categories ──────────────────────────────────────────────

        public enum GateSize { Small, Medium, Large }

        private class GateSlot
        {
            public string   Name      { get; }
            public string   Terminal  { get; }
            public GateSize Size      { get; }
            public double   X         { get; }
            public double   Y         { get; }
            public string?  Occupant  { get; set; }

            public bool IsFree => Occupant == null;

            public GateSlot(string name, string terminal, GateSize size, double x, double y)
            {
                Name     = name;
                Terminal = terminal;
                Size     = size;
                X        = x;
                Y        = y;
            }
        }

        // ── Full TLV gate inventory (matches airport-layout-tlv.json) ─────────

        private readonly List<GateSlot> _gates = new()
        {
            // ── Terminal A — Domestic & Charter ──────────────────────────────
            new("A1",  "A", GateSize.Small,  160,  400),
            new("A2",  "A", GateSize.Small,  220,  400),
            new("A3",  "A", GateSize.Medium, 280,  400),
            new("A4",  "A", GateSize.Medium, 340,  400),
            new("A5",  "A", GateSize.Small,  400,  400),
            new("A6",  "A", GateSize.Small,  460,  400),

            // ── Terminal B — International ────────────────────────────────────
            new("B1",  "B", GateSize.Large,  560,  400),
            new("B2",  "B", GateSize.Large,  620,  400),
            new("B3",  "B", GateSize.Medium, 680,  400),
            new("B4",  "B", GateSize.Medium, 740,  400),
            new("B5",  "B", GateSize.Medium, 800,  400),
            new("B6",  "B", GateSize.Large,  860,  400),

            // ── Terminal C — Long Haul ────────────────────────────────────────
            new("C1",  "C", GateSize.Large,  980,  400),
            new("C2",  "C", GateSize.Large,  1040, 400),
            new("C3",  "C", GateSize.Medium, 1100, 400),
            new("C4",  "C", GateSize.Medium, 1160, 400),
            new("C12", "C", GateSize.Large,  1220, 400),
            new("C14", "C", GateSize.Large,  1280, 400),

            // ── Terminal D — El Al Hub ────────────────────────────────────────
            new("D1",  "D", GateSize.Large,  1510, 400),
            new("D2",  "D", GateSize.Large,  1570, 400),
            new("D7",  "D", GateSize.Large,  1630, 400),
            new("D8",  "D", GateSize.Large,  1690, 400),
            new("D9",  "D", GateSize.Large,  1750, 400),
            new("D10", "D", GateSize.Large,  1810, 400),
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Assign a gate to a flight.
        /// Priority order:
        ///   1. Preferred gate by name (from FlightEvent.Gate) if free
        ///   2. Any free gate that fits the aircraft size
        ///   3. Any free gate (fallback — avoids stranding an aircraft)
        /// Returns null only when the airport is completely full.
        /// </summary>
        public (string gateName, double gateX, double gateY)? Assign(
            string flightId, string preferredGate, AircraftType aircraftType)
        {
            // 1. Preferred gate by name
            var preferred = _gates.FirstOrDefault(
                g => g.Name == preferredGate && g.IsFree);

            if (preferred != null)
            {
                preferred.Occupant = flightId;
                return (preferred.Name, preferred.X, preferred.Y);
            }

            // 2. Size-matched fallback
            GateSize ideal = SizeFor(aircraftType);

            var sizeFit = _gates.FirstOrDefault(g => g.IsFree && g.Size == ideal);
            if (sizeFit != null)
            {
                sizeFit.Occupant = flightId;
                return (sizeFit.Name, sizeFit.X, sizeFit.Y);
            }

            // 3. Any free gate
            var anyFree = _gates.FirstOrDefault(g => g.IsFree);
            if (anyFree != null)
            {
                anyFree.Occupant = flightId;
                return (anyFree.Name, anyFree.X, anyFree.Y);
            }

            // Airport full
            return null;
        }

        /// <summary>
        /// Legacy overload — called without AircraftType (defaults to Medium).
        /// Retained for compatibility while existing callers are updated.
        /// </summary>
        public (string gateName, double gateX, double gateY)? Assign(
            string flightId, string preferredGate)
            => Assign(flightId, preferredGate, AircraftType.Medium);

        public void Release(string flightId)
        {
            var slot = _gates.FirstOrDefault(g => g.Occupant == flightId);
            if (slot != null) slot.Occupant = null;
        }

        public bool HasFreeGate() => _gates.Any(g => g.IsFree);

        public int FreeGateCount()     => _gates.Count(g => g.IsFree);
        public int OccupiedGateCount() => _gates.Count(g => !g.IsFree);

        /// <summary>
        /// Snapshot list for the renderer — gate name, world position, occupant.
        /// </summary>
        public IReadOnlyList<(string name, double x, double y, string? occupant)>
            GetSnapshot() =>
            _gates.Select(g => (g.Name, g.X, g.Y, g.Occupant)).ToList();

        /// <summary>
        /// Gates grouped by terminal — useful for UI panels and alert logic.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<(string name, bool occupied)>>
            GetTerminalSnapshot()
        {
            return _gates
                .GroupBy(g => g.Terminal)
                .ToDictionary(
                    grp => grp.Key,
                    grp => (IReadOnlyList<(string, bool)>)
                        grp.Select(g => (g.Name, g.Occupant != null)).ToList());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static GateSize SizeFor(AircraftType type) => type switch
        {
            AircraftType.Small  => GateSize.Small,
            AircraftType.Medium => GateSize.Medium,
            AircraftType.Large  => GateSize.Large,
            _                   => GateSize.Medium
        };
    }
}