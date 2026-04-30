using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    /// <summary>
    /// Tracks which gates are occupied and assigns stands to new aircraft.
    /// Supports dynamic loading of airport layouts via JSON.
    /// </summary>
    public class GateManager
    {
        // ── Gate size categories ──────────────────────────────────────────────

        public enum GateSize { Small, Medium, Large }

        // Added public set to properties so JSON deserializer can populate them
        public class GateSlot
        {
            public string   Name      { get; set; } = string.Empty;
            public string   Terminal  { get; set; } = string.Empty;
            public GateSize Size      { get; set; }
            public double   X         { get; set; }
            public double   Y         { get; set; }
            public string?  Occupant  { get; set; }

            public bool IsFree => Occupant == null;

            // Parameterless constructor needed for System.Text.Json
            public GateSlot() { }

            public GateSlot(string name, string terminal, GateSize size, double x, double y)
            {
                Name     = name;
                Terminal = terminal;
                Size     = size;
                X        = x;
                Y        = y;
            }
        }

        // ── Layout loading schema ─────────────────────────────────────────────

        // This matches the structure of your airport-layout-*.json files
        private class LayoutData
        {
            public string AirportCode { get; set; } = string.Empty;
            public List<GateSlot> Gates { get; set; } = new();
        }

        private List<GateSlot> _gates = new();

        public GateManager()
        {
            // Default to TLV on startup if no layout is specified
            LoadLayout("tlv");
        }

        // ── Dynamic Layout Loading ────────────────────────────────────────────

        public void LoadLayout(string layoutId)
        {
            try
            {
                // Note: Ensure your JSON files have "Copy to Output Directory" set to "PreserveNewest"
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string fileName = $"airport-layout-{layoutId.ToLower()}.json";
                string fullPath = Path.Combine(basePath, fileName);

                if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    var layoutOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    // We deserialize directly to List<GateSlot> assuming your JSON is just an array of gate objects.
                    // If your JSON has a root object (like {"Gates": [...]}), use the LayoutData wrapper class above.
                    // Assuming flat array based on typical setup:
                    var loadedGates = JsonSerializer.Deserialize<List<GateSlot>>(json, layoutOptions);

                    if (loadedGates != null && loadedGates.Any())
                    {
                        _gates = loadedGates;
                        Console.WriteLine($"[GateManager] Successfully loaded {loadedGates.Count} gates for layout: {layoutId.ToUpper()}");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"[GateManager] WARNING: Layout file not found at {fullPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GateManager] ERROR loading layout '{layoutId}': {ex.Message}");
            }

            // Fallback to hardcoded TLV gates if file loading fails
            if (!_gates.Any())
            {
                Console.WriteLine("[GateManager] Falling back to hardcoded TLV gate layout.");
                LoadHardcodedTlvGates();
            }
        }

        private void LoadHardcodedTlvGates()
        {
            _gates = new List<GateSlot>
            {
                // Terminal A
                new("A1",  "A", GateSize.Small,  160,  400),
                new("A2",  "A", GateSize.Small,  220,  400),
                new("A3",  "A", GateSize.Medium, 280,  400),
                new("A4",  "A", GateSize.Medium, 340,  400),
                new("A5",  "A", GateSize.Small,  400,  400),
                new("A6",  "A", GateSize.Small,  460,  400),

                // Terminal B
                new("B1",  "B", GateSize.Large,  560,  400),
                new("B2",  "B", GateSize.Large,  620,  400),
                new("B3",  "B", GateSize.Medium, 680,  400),
                new("B4",  "B", GateSize.Medium, 740,  400),
                new("B5",  "B", GateSize.Medium, 800,  400),
                new("B6",  "B", GateSize.Large,  860,  400),

                // Terminal C
                new("C1",  "C", GateSize.Large,  980,  400),
                new("C2",  "C", GateSize.Large,  1040, 400),
                new("C3",  "C", GateSize.Medium, 1100, 400),
                new("C4",  "C", GateSize.Medium, 1160, 400),
                new("C12", "C", GateSize.Large,  1220, 400),
                new("C14", "C", GateSize.Large,  1280, 400),

                // Terminal D
                new("D1",  "D", GateSize.Large,  1510, 400),
                new("D2",  "D", GateSize.Large,  1570, 400),
                new("D7",  "D", GateSize.Large,  1630, 400),
                new("D8",  "D", GateSize.Large,  1690, 400),
                new("D9",  "D", GateSize.Large,  1750, 400),
                new("D10", "D", GateSize.Large,  1810, 400),
            };
        }


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