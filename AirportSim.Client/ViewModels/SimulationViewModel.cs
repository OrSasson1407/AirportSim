using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using AirportSim.Shared.Models;
using AirportSim.Client.Connection;

namespace AirportSim.Client.ViewModels
{
    public class SimulationViewModel
    {
        // ── Connection ────────────────────────────────────────────────────────
        public readonly SimulationConnection Connection;

        // ── Snapshot state ────────────────────────────────────────────────────
        public SimSnapshot? PreviousSnapshot { get; private set; }
        public SimSnapshot? TargetSnapshot   { get; private set; }
        public DateTime     LastSnapshotTime { get; private set; }

        public bool IsConnected { get; private set; }

        // ── Alert queue ───────────────────────────────────────────────────────
        private readonly List<string> _alertQueue = new();
        private const int AlertQueueMax = 30;
        public IReadOnlyList<string> AlertQueue => _alertQueue;

        // ── Dashboard history data ────────────────────────────────────────────
        private readonly List<(DateTime SimTime, int Count)> _trafficHistory = new();
        private DateTime _lastTrafficSample = DateTime.MinValue;
        private const int TrafficHistoryMaxSamples = 20;   // 20 × 30s = 10 min

        private readonly Dictionary<WeatherCondition, int> _goAroundsByWeather = new()
        {
            [WeatherCondition.Clear]  = 0,
            [WeatherCondition.Cloudy] = 0,
            [WeatherCondition.Rain]   = 0,
            [WeatherCondition.Fog]    = 0,
            [WeatherCondition.Storm]  = 0,
        };
        private int _lastGoAroundCount = 0;

        private int _rwySamplesTotal;
        private int _rwy0OccupiedSamples;
        private int _rwy1OccupiedSamples;

        public IReadOnlyList<(DateTime SimTime, int Count)> TrafficHistory => _trafficHistory;
        public IReadOnlyDictionary<WeatherCondition, int>   GoAroundsByWeather => _goAroundsByWeather;
        public double Runway0Utilisation => _rwySamplesTotal == 0 ? 0.0
            : (double)_rwy0OccupiedSamples / _rwySamplesTotal;
        public double Runway1Utilisation => _rwySamplesTotal == 0 ? 0.0
            : (double)_rwy1OccupiedSamples / _rwySamplesTotal;

        public event Action? StateChanged;
        public event Action<string>? EmergencyDetected;

        private readonly HashSet<string> _knownEmergencies = new();

        public SimulationViewModel()
        {
            Connection = new SimulationConnection();

            Connection.OnSnapshotReceived += HandleNewSnapshot;
            Connection.OnAlertReceived    += HandleAlert;
            Connection.OnConnected        += () => SetConnected(true);
            Connection.OnDisconnected     += () => SetConnected(false);
        }

        public async void Start()
        {
            await Connection.ConnectAsync();
        }

        // ── Interpolation ─────────────────────────────────────────────────────

        public double GetInterpolationT()
        {
            if (TargetSnapshot == null || PreviousSnapshot == null) return 0;
            double elapsed = (DateTime.UtcNow - LastSnapshotTime).TotalMilliseconds;
            return Math.Clamp(elapsed / 200.0, 0.0, 1.0);
        }

        public (double x, double y, double heading)? GetInterpolatedPosition(string flightId)
        {
            if (TargetSnapshot == null) return null;

            double t = GetInterpolationT();

            var target = TargetSnapshot.ActiveAircraft.FirstOrDefault(a => a.FlightId == flightId);
            if (target == null) return null;

            var prev = PreviousSnapshot?.ActiveAircraft.FirstOrDefault(a => a.FlightId == flightId)
                       ?? target;

            double x       = prev.Position.X + (target.Position.X - prev.Position.X) * t;
            double y       = prev.Position.Y + (target.Position.Y - prev.Position.Y) * t;
            double heading = LerpAngle(prev.Heading, target.Heading, t);

            return (x, y, heading);
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private void HandleNewSnapshot(SimSnapshot snapshot)
        {
            PreviousSnapshot = TargetSnapshot ?? snapshot;
            TargetSnapshot   = snapshot;
            LastSnapshotTime = DateTime.UtcNow;

            foreach (var alert in snapshot.RecentAlerts)
                if (!_alertQueue.Contains(alert))
                    PushAlert(alert);

            foreach (var ac in snapshot.ActiveAircraft)
            {
                if (ac.Status == AircraftStatus.Emergency &&
                    !_knownEmergencies.Contains(ac.FlightId))
                {
                    _knownEmergencies.Add(ac.FlightId);
                    Dispatcher.UIThread.Post(() =>
                        EmergencyDetected?.Invoke(ac.FlightId));
                }
            }

            var activeIds = snapshot.ActiveAircraft.Select(a => a.FlightId).ToHashSet();
            _knownEmergencies.IntersectWith(activeIds);

            // ── Dashboard: sample traffic history every 30 sim-seconds ────────
            if ((snapshot.SimulatedTime - _lastTrafficSample).TotalSeconds >= 30)
            {
                _lastTrafficSample = snapshot.SimulatedTime;
                _trafficHistory.Add((snapshot.SimulatedTime, snapshot.ActiveAircraft.Count));
                if (_trafficHistory.Count > TrafficHistoryMaxSamples)
                    _trafficHistory.RemoveAt(0);
            }

            // ── Dashboard: attribute new go-arounds to current weather ────────
            int newGoArounds = snapshot.GoAroundsToday - _lastGoAroundCount;
            if (newGoArounds > 0)
            {
                _goAroundsByWeather[snapshot.Weather] =
                    _goAroundsByWeather.GetValueOrDefault(snapshot.Weather) + newGoArounds;
            }
            _lastGoAroundCount = snapshot.GoAroundsToday;

            // ── Dashboard: runway utilisation sample ──────────────────────────
            _rwySamplesTotal++;
            if (snapshot.Runways.Count > 0 && snapshot.Runways[0].Status == RunwayStatus.Occupied)
                _rwy0OccupiedSamples++;
            if (snapshot.Runways.Count > 1 && snapshot.Runways[1].Status == RunwayStatus.Occupied)
                _rwy1OccupiedSamples++;
        }

        private void HandleAlert(string message) => PushAlert(message);

        private void SetConnected(bool connected)
        {
            IsConnected = connected;
            if (!connected)
                PushAlert("⚠ Disconnected from server — reconnecting...");
            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        private void PushAlert(string message)
        {
            _alertQueue.Insert(0, message);
            if (_alertQueue.Count > AlertQueueMax)
                _alertQueue.RemoveAt(_alertQueue.Count - 1);
            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        // ── Convenience properties ────────────────────────────────────────────

        public string SimTimeText => TargetSnapshot != null
            ? $"{TargetSnapshot.SimulatedTime:HH:mm:ss}" : "--:--:--";

        public string SpeedText => TargetSnapshot != null
            ? $"{TargetSnapshot.TimeScale}×" : "–";

        public string WeatherText => TargetSnapshot != null
            ? WeatherIcon(TargetSnapshot.Weather) : "";

        public bool HasActiveEmergency => TargetSnapshot?.ActiveAircraft
            .Any(a => a.Status == AircraftStatus.Emergency) ?? false;

        private static string WeatherIcon(WeatherCondition w) => w switch
        {
            WeatherCondition.Clear  => "☀ Clear",
            WeatherCondition.Cloudy => "⛅ Cloudy",
            WeatherCondition.Rain   => "🌧 Rain",
            WeatherCondition.Fog    => "🌫 Fog",
            WeatherCondition.Storm  => "⛈ Storm",
            _                       => w.ToString()
        };

        private static double LerpAngle(double a, double b, double t)
        {
            double diff = b - a;
            while (diff < -180) diff += 360;
            while (diff >  180) diff -= 360;
            return a + diff * t;
        }
    }
}