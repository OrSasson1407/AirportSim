using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Simulation
{
    /// <summary>
    /// Runs every simulation tick and checks for:
    ///   1. Separation violations — two airborne aircraft too close in the
    ///      approach corridor (horizontal distance < threshold).
    ///   2. Runway incursion attempts — a departing aircraft begins its
    ///      takeoff roll while an arriving aircraft is still on the runway.
    ///   3. Go-around cascade — three or more go-arounds in the last
    ///      5 simulated minutes (indicates severe congestion).
    /// Detected events are written to PendingAlerts; the engine drains them
    /// each tick just like RunwayController.PendingAlerts.
    /// </summary>
    public class ConflictDetector
    {
        // ── Separation thresholds (world units) ───────────────────────────────
        // World space is 2000 × 600.  A "world unit" ≈ 0.5 m at real scale.
        // 120 wu ≈ 3 NM minimum radar separation.
        private const double AirborneMinSeparation  = 120.0;
        private const double FinalMinSeparation     = 180.0;  // tighter on final

        // ── Go-around cascade window (sim milliseconds) ───────────────────────
        private const double CascadeWindowMs   = 5 * 60_000;
        private const int    CascadeThreshold  = 3;

        // ── State ─────────────────────────────────────────────────────────────

        // Tracks which conflict pairs we've already reported so we don't
        // spam the alert log every tick (suppress for 30 sim-seconds after report)
        private readonly Dictionary<string, double> _suppressedPairs  = new();
        private const double SuppressForMs = 30_000;

        // Ring buffer of go-around sim-times for cascade detection
        private readonly Queue<double> _recentGoArounds = new();

        // Set of flight IDs we've already reported a cascade for this window
        private bool _cascadeReported = false;
        private double _lastCascadeResetMs = 0;

        public readonly List<string> PendingAlerts = new();

        // ── Main check — called every engine tick ─────────────────────────────

        public void Check(IReadOnlyList<AircraftState> aircraft,
                          double simNowMs,
                          double simDeltaMs)
        {
            PendingAlerts.Clear();

            TickSuppression(simDeltaMs);
            CheckSeparation(aircraft, simNowMs);
            CheckRunwayIncursion(aircraft, simNowMs);
            CheckGoAroundCascade(aircraft, simNowMs, simDeltaMs);
        }

        // ── Called by SimulationEngine when a go-around just happened ─────────
        public void RecordGoAround(double simNowMs)
        {
            _recentGoArounds.Enqueue(simNowMs);
        }

        // ── Separation ────────────────────────────────────────────────────────

        private void CheckSeparation(IReadOnlyList<AircraftState> aircraft,
                                     double simNowMs)
        {
            // Only check airborne arrivals — approaching / on-final / go-around
            var airborne = aircraft
                .Where(a => a.FlightType == FlightType.Arrival &&
                            a.Phase is AircraftPhase.Approaching
                                    or AircraftPhase.OnFinal
                                    or AircraftPhase.GoAround)
                .ToList();

            for (int i = 0; i < airborne.Count; i++)
            for (int j = i + 1; j < airborne.Count; j++)
            {
                var a = airborne[i];
                var b = airborne[j];

                double dist = Distance(a.Position, b.Position);

                // Use tighter separation on final approach
                bool onFinal = a.Phase == AircraftPhase.OnFinal ||
                               b.Phase == AircraftPhase.OnFinal;
                double threshold = onFinal
                    ? FinalMinSeparation
                    : AirborneMinSeparation;

                if (dist < threshold)
                {
                    string pairKey = PairKey(a.FlightId, b.FlightId);
                    if (!_suppressedPairs.ContainsKey(pairKey))
                    {
                        string severity = dist < threshold * 0.5 ? "🔴 COLLISION ALERT" : "🟡 SEPARATION";
                        PendingAlerts.Add(
                            $"{severity}: {a.FlightId} / {b.FlightId} " +
                            $"— {dist:F0} wu ({(onFinal ? "final" : "approach")})");
                        _suppressedPairs[pairKey] = SuppressForMs;
                    }
                }
            }

            // Also check arriving vs departing aircraft in the climb/approach zone
            var departing = aircraft
                .Where(a => a.FlightType == FlightType.Departure &&
                            a.Phase is AircraftPhase.Climbing)
                .ToList();

            foreach (var arr in airborne)
            foreach (var dep in departing)
            {
                double dist    = Distance(arr.Position, dep.Position);
                string pairKey = PairKey(arr.FlightId, dep.FlightId);

                if (dist < AirborneMinSeparation &&
                    !_suppressedPairs.ContainsKey(pairKey))
                {
                    PendingAlerts.Add(
                        $"🟡 SEPARATION: {arr.FlightId} (ARR) / {dep.FlightId} (DEP) " +
                        $"— {dist:F0} wu");
                    _suppressedPairs[pairKey] = SuppressForMs;
                }
            }
        }

        // ── Runway incursion ──────────────────────────────────────────────────

        private void CheckRunwayIncursion(IReadOnlyList<AircraftState> aircraft,
                                          double simNowMs)
        {
            // An incursion occurs when a departure begins its takeoff roll
            // (phase == Takeoff) while an arrival is still rolling out
            // (phase == Landing or Rollout).  The two runways are separate
            // in Step 1, but the taxiway crossing zone is shared.

            bool anyLanding = aircraft.Any(
                a => a.FlightType == FlightType.Arrival &&
                     a.Phase is AircraftPhase.Landing or AircraftPhase.Rollout);

            bool anyTakingOff = aircraft.Any(
                a => a.FlightType == FlightType.Departure &&
                     a.Phase == AircraftPhase.Takeoff &&
                     a.PhaseProgress < 0.15);   // just started the roll

            if (anyLanding && anyTakingOff)
            {
                const string key = "runway_incursion";
                if (!_suppressedPairs.ContainsKey(key))
                {
                    PendingAlerts.Add(
                        "🔴 RUNWAY INCURSION: departure rolling while arrival on runway!");
                    _suppressedPairs[key] = SuppressForMs;
                }
            }
        }

        // ── Go-around cascade ─────────────────────────────────────────────────

        private void CheckGoAroundCascade(IReadOnlyList<AircraftState> aircraft,
                                          double simNowMs,
                                          double simDeltaMs)
        {
            // Reset cascade window every CascadeWindowMs
            if (simNowMs - _lastCascadeResetMs > CascadeWindowMs)
            {
                _recentGoArounds.Clear();
                _cascadeReported      = false;
                _lastCascadeResetMs   = simNowMs;
            }

            // Prune entries older than the window
            while (_recentGoArounds.Count > 0 &&
                   simNowMs - _recentGoArounds.Peek() > CascadeWindowMs)
                _recentGoArounds.Dequeue();

            if (!_cascadeReported &&
                _recentGoArounds.Count >= CascadeThreshold)
            {
                PendingAlerts.Add(
                    $"⚠ GO-AROUND CASCADE: {_recentGoArounds.Count} go-arounds " +
                    $"in last 5 sim-minutes — possible approach congestion");
                _cascadeReported = true;
            }
        }

        // ── Suppression ticker ────────────────────────────────────────────────

        private void TickSuppression(double simDeltaMs)
        {
            var expired = new List<string>();
            foreach (var kv in _suppressedPairs)
            {
                _suppressedPairs[kv.Key] -= simDeltaMs;
                if (_suppressedPairs[kv.Key] <= 0)
                    expired.Add(kv.Key);
            }
            foreach (var k in expired)
                _suppressedPairs.Remove(k);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static double Distance(SimPoint a, SimPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string PairKey(string a, string b) =>
            string.Compare(a, b, StringComparison.Ordinal) < 0
                ? $"{a}|{b}"
                : $"{b}|{a}";
    }
}