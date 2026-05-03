using AirportSim.Shared.Models;

namespace AirportSim.Server.Infrastructure.Simulation;

public class ConflictDetector
{
    private const double AirborneMinSeparation = 120.0;
    private const double FinalMinSeparation    = 180.0;
    private const double GroundMinSeparation   = 30.0;

    private const double CascadeWindowMs  = 5 * 60_000;
    private const int    CascadeThreshold = 3;

    private readonly Dictionary<string, double> _suppressedPairs = new();
    private const double SuppressForMs = 30_000;

    private readonly Queue<double> _recentGoArounds = new();
    private bool   _cascadeReported    = false;
    private double _lastCascadeResetMs = 0;

    public readonly List<string> PendingAlerts = new();
    public int TotalConflicts { get; private set; }

    public void Check(IReadOnlyList<AircraftState> aircraft, double simNowMs, double simDeltaMs)
    {
        PendingAlerts.Clear();
        TickSuppression(simDeltaMs);
        CheckSeparation(aircraft, simNowMs);
        CheckRunwayIncursion(aircraft, simNowMs);
        CheckGroundConflicts(aircraft, simNowMs);
        CheckGoAroundCascade(aircraft, simNowMs, simDeltaMs);
    }

    public void RecordGoAround(double simNowMs) =>
        _recentGoArounds.Enqueue(simNowMs);

    private void CheckSeparation(IReadOnlyList<AircraftState> aircraft, double simNowMs)
    {
        var airborne = aircraft
            .Where(a => a.FlightType == FlightType.Arrival &&
                        a.Phase is AircraftPhase.Approaching
                                or AircraftPhase.OnFinal
                                or AircraftPhase.GoAround)
            .ToList();

        for (int i = 0; i < airborne.Count; i++)
        for (int j = i + 1; j < airborne.Count; j++)
        {
            var    a         = airborne[i];
            var    b         = airborne[j];
            double dist      = Distance(a.Position, b.Position);
            bool   onFinal   = a.Phase == AircraftPhase.OnFinal || b.Phase == AircraftPhase.OnFinal;
            double threshold = onFinal ? FinalMinSeparation : AirborneMinSeparation;

            if (dist < threshold)
            {
                string pairKey = PairKey(a.FlightId, b.FlightId);
                if (!_suppressedPairs.ContainsKey(pairKey))
                {
                    string severity = dist < threshold * 0.5 ? "🔴 COLLISION ALERT" : "🟡 SEPARATION";
                    PendingAlerts.Add(
                        $"{severity}: {a.FlightId} / {b.FlightId} — {dist:F0} wu ({(onFinal ? "final" : "approach")})");
                    _suppressedPairs[pairKey] = SuppressForMs;
                    TotalConflicts++;
                }
            }
        }

        var departing = aircraft
            .Where(a => a.FlightType == FlightType.Departure && a.Phase == AircraftPhase.Climbing)
            .ToList();

        foreach (var arr in airborne)
        foreach (var dep in departing)
        {
            double dist    = Distance(arr.Position, dep.Position);
            string pairKey = PairKey(arr.FlightId, dep.FlightId);
            if (dist < AirborneMinSeparation && !_suppressedPairs.ContainsKey(pairKey))
            {
                PendingAlerts.Add(
                    $"🟡 SEPARATION: {arr.FlightId} (ARR) / {dep.FlightId} (DEP) — {dist:F0} wu");
                _suppressedPairs[pairKey] = SuppressForMs;
                TotalConflicts++;
            }
        }
    }

    private void CheckRunwayIncursion(IReadOnlyList<AircraftState> aircraft, double simNowMs)
    {
        bool anyLanding   = aircraft.Any(a =>
            a.FlightType == FlightType.Arrival &&
            a.Phase is AircraftPhase.Landing or AircraftPhase.Rollout);

        bool anyTakingOff = aircraft.Any(a =>
            a.FlightType == FlightType.Departure &&
            a.Phase == AircraftPhase.Takeoff && a.PhaseProgress < 0.15);

        if (anyLanding && anyTakingOff)
        {
            const string key = "runway_incursion";
            if (!_suppressedPairs.ContainsKey(key))
            {
                PendingAlerts.Add("🔴 RUNWAY INCURSION: departure rolling while arrival on runway!");
                _suppressedPairs[key] = SuppressForMs;
                TotalConflicts += 3;
            }
        }
    }

    private void CheckGroundConflicts(IReadOnlyList<AircraftState> aircraft, double simNowMs)
    {
        var ground = aircraft
            .Where(a => a.Phase is AircraftPhase.Taxiing or AircraftPhase.Pushback)
            .ToList();

        for (int i = 0; i < ground.Count; i++)
        for (int j = i + 1; j < ground.Count; j++)
        {
            double dist    = Distance(ground[i].Position, ground[j].Position);
            string pairKey = PairKey(ground[i].FlightId, ground[j].FlightId);
            if (dist < GroundMinSeparation && !_suppressedPairs.ContainsKey(pairKey))
            {
                PendingAlerts.Add(
                    $"🟠 TAXIWAY CONFLICT: {ground[i].FlightId} and {ground[j].FlightId} — {dist:F0} wu!");
                _suppressedPairs[pairKey] = SuppressForMs;
                TotalConflicts++;
            }
        }
    }

    private void CheckGoAroundCascade(
        IReadOnlyList<AircraftState> aircraft, double simNowMs, double simDeltaMs)
    {
        if (simNowMs - _lastCascadeResetMs > CascadeWindowMs)
        {
            _recentGoArounds.Clear();
            _cascadeReported    = false;
            _lastCascadeResetMs = simNowMs;
        }

        while (_recentGoArounds.Count > 0 && simNowMs - _recentGoArounds.Peek() > CascadeWindowMs)
            _recentGoArounds.Dequeue();

        if (!_cascadeReported && _recentGoArounds.Count >= CascadeThreshold)
        {
            PendingAlerts.Add(
                $"⚠ GO-AROUND CASCADE: {_recentGoArounds.Count} go-arounds in last 5 sim-minutes — possible approach congestion");
            _cascadeReported = true;
        }
    }

    private void TickSuppression(double simDeltaMs)
    {
        var expired = new List<string>();
        foreach (var kv in _suppressedPairs)
        {
            _suppressedPairs[kv.Key] -= simDeltaMs;
            if (_suppressedPairs[kv.Key] <= 0) expired.Add(kv.Key);
        }
        foreach (var k in expired) _suppressedPairs.Remove(k);
    }

    private static double Distance(SimPoint a, SimPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string PairKey(string a, string b) =>
        string.Compare(a, b, StringComparison.Ordinal) < 0 ? $"{a}|{b}" : $"{b}|{a}";
}