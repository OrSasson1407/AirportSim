using System.Text.Json;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Infrastructure.Simulation;

public class Taxiway
{
    public string Name { get; set; } = string.Empty;
    public string Use  { get; set; } = string.Empty;
    public List<SimPoint> Path { get; set; } = new();
}

public class GateManager
{
    public enum GateSize { Small, Medium, Large }

    public class GateSlot
    {
        public string   Name     { get; set; } = string.Empty;
        public string   Terminal { get; set; } = string.Empty;
        public GateSize Size     { get; set; }
        public double   X        { get; set; }
        public double   Y        { get; set; }
        public string?  Occupant { get; set; }

        public bool IsFree => Occupant == null;

        public GateSlot() { }
        public GateSlot(string name, string terminal, GateSize size, double x, double y)
        {
            Name = name; Terminal = terminal; Size = size; X = x; Y = y;
        }
    }

    private List<GateSlot> _gates = new();

    public List<Taxiway> Taxiways { get; private set; } = new();
    public Dictionary<string, List<SimPoint>> GroundRoutes { get; private set; } = new();

    public GateManager()
    {
        LoadLayout("tlv");
    }

    public void LoadLayout(string layoutId)
    {
        _gates.Clear();
        Taxiways.Clear();
        GroundRoutes.Clear();

        try
        {
            string basePath  = AppDomain.CurrentDomain.BaseDirectory;
            string fileName  = (layoutId.ToLower() is "lhr" or "jfk")
                ? "airport-layouts-lhr-jfk.json"
                : $"airport-layout-{layoutId.ToLower()}.json";
            string fullPath  = Path.Combine(basePath, fileName);

            if (File.Exists(fullPath))
            {
                string      json       = File.ReadAllText(fullPath);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root       = doc.RootElement;
                JsonElement airportNode = root;

                if (root.TryGetProperty("Airports", out JsonElement airportsEl))
                {
                    string searchKey = layoutId.ToUpper() switch
                    {
                        "LHR" => "EGLL",
                        "JFK" => "KJFK",
                        _     => layoutId.ToUpper()
                    };
                    if (airportsEl.TryGetProperty(searchKey, out JsonElement specific))
                        airportNode = specific;
                }

                if (airportNode.TryGetProperty("Gates", out JsonElement gatesEl))
                {
                    foreach (var gateProp in gatesEl.EnumerateObject())
                    {
                        if (gateProp.Name.StartsWith("_")) continue;
                        var g = gateProp.Value;
                        _gates.Add(new GateSlot
                        {
                            Name     = gateProp.Name,
                            Terminal = g.GetProperty("Terminal").GetString() ?? "",
                            Size     = Enum.TryParse<GateSize>(g.GetProperty("Size").GetString(), out var s)
                                       ? s : GateSize.Medium,
                            X        = g.GetProperty("X").GetDouble(),
                            Y        = g.GetProperty("Y").GetDouble()
                        });
                    }
                }

                if (airportNode.TryGetProperty("Taxiways", out JsonElement twyEl))
                {
                    foreach (var twyProp in twyEl.EnumerateObject())
                    {
                        if (twyProp.Name.StartsWith("_")) continue;
                        var t   = twyProp.Value;
                        var twy = new Taxiway
                        {
                            Name = twyProp.Name,
                            Use  = t.GetProperty("Use").GetString() ?? ""
                        };
                        if (t.TryGetProperty("Path", out JsonElement pathEl))
                            foreach (var pt in pathEl.EnumerateArray())
                                twy.Path.Add(new SimPoint(
                                    pt.GetProperty("X").GetDouble(),
                                    pt.GetProperty("Y").GetDouble()));
                        Taxiways.Add(twy);
                    }
                }

                if (airportNode.TryGetProperty("GroundVehicleRoutes", out JsonElement gvEl))
                {
                    foreach (var gvProp in gvEl.EnumerateObject())
                    {
                        if (gvProp.Name.StartsWith("_")) continue;
                        var path = new List<SimPoint>();
                        foreach (var pt in gvProp.Value.EnumerateArray())
                            path.Add(new SimPoint(
                                pt.GetProperty("X").GetDouble(),
                                pt.GetProperty("Y").GetDouble()));
                        GroundRoutes[gvProp.Name] = path;
                    }
                }

                Console.WriteLine(
                    $"[GateManager] Loaded {_gates.Count} gates, {Taxiways.Count} taxiways, " +
                    $"{GroundRoutes.Count} vehicle routes for {layoutId.ToUpper()}");
                return;
            }

            Console.WriteLine($"[GateManager] WARNING: Layout file not found at {fullPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GateManager] ERROR loading layout '{layoutId}': {ex.Message}");
        }

        if (!_gates.Any())
        {
            Console.WriteLine("[GateManager] Falling back to hardcoded TLV gate layout.");
            LoadHardcodedTlvGates();
        }
    }

    // ── Engine-facing API ─────────────────────────────────────────────────────

    /// <summary>Assign a gate to a flight. Returns the gate name, or null if none free.</summary>
    public string? AssignGate(string flightId, AircraftType aircraftType)
    {
        GateSize ideal = SizeFor(aircraftType);

        var slot = _gates.FirstOrDefault(g => g.IsFree && g.Size == ideal)
                ?? _gates.FirstOrDefault(g => g.IsFree);

        if (slot == null) return null;
        slot.Occupant = flightId;
        return slot.Name;
    }

    /// <summary>Returns the (x, y) world position of a named gate.</summary>
    public (double x, double y) GetGatePosition(string gateName)
    {
        var slot = _gates.FirstOrDefault(g => g.Name == gateName);
        return slot != null ? (slot.X, slot.Y) : (300.0, 400.0);
    }

    public void Release(string flightId)
    {
        var slot = _gates.FirstOrDefault(g => g.Occupant == flightId);
        if (slot != null) slot.Occupant = null;
    }

    // ── Route helpers ─────────────────────────────────────────────────────────

    public List<SimPoint> GetArrivalRoute(SimPoint rolloutEnd, SimPoint gate)
    {
        var route    = new List<SimPoint> { rolloutEnd };
        var mainTwy  = Taxiways.FirstOrDefault(
            t => t.Use.Contains("Arrival", StringComparison.OrdinalIgnoreCase));

        if (mainTwy != null && mainTwy.Path.Any())
        {
            route.AddRange(mainTwy.Path);
            route.Add(new SimPoint(gate.X, mainTwy.Path.Last().Y));
        }
        else
        {
            route.Add(new SimPoint(gate.X, rolloutEnd.Y));
        }
        route.Add(gate);
        return route;
    }

    public List<SimPoint> GetDepartureRoute(SimPoint gate)
    {
        var route   = new List<SimPoint> { new SimPoint(gate.X, gate.Y + 30) };
        var mainTwy = Taxiways.FirstOrDefault(t =>
            t.Use.Contains("Depart",  StringComparison.OrdinalIgnoreCase) ||
            t.Use.Contains("hold",    StringComparison.OrdinalIgnoreCase) ||
            t.Use.Contains("queue",   StringComparison.OrdinalIgnoreCase));

        if (mainTwy != null && mainTwy.Path.Any())
        {
            route.Add(new SimPoint(gate.X, mainTwy.Path.First().Y));
            route.AddRange(mainTwy.Path);
        }
        else
        {
            route.Add(new SimPoint(gate.X, 500));
            route.Add(new SimPoint(390, 500));
        }
        return route;
    }

    // ── Snapshot / info ───────────────────────────────────────────────────────

    public bool HasFreeGate()  => _gates.Any(g => g.IsFree);
    public int  FreeGateCount()  => _gates.Count(g => g.IsFree);
    public int  OccupiedGateCount() => _gates.Count(g => !g.IsFree);

    public IReadOnlyList<(string name, double x, double y, string? occupant)> GetSnapshot() =>
        _gates.Select(g => (g.Name, g.X, g.Y, g.Occupant)).ToList();

    // ── Private helpers ───────────────────────────────────────────────────────

    private static GateSize SizeFor(AircraftType type) => type switch
    {
        AircraftType.Small => GateSize.Small,
        AircraftType.Large => GateSize.Large,
        _                  => GateSize.Medium
    };

    private void LoadHardcodedTlvGates()
    {
        _gates = new List<GateSlot>
        {
            new("A1", "A", GateSize.Small,  160, 400),
            new("A3", "A", GateSize.Medium, 280, 400),
            new("B1", "B", GateSize.Large,  560, 400),
        };
    }
}