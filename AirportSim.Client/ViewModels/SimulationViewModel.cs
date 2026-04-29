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

        // NEW: connection health flag read by the UI status bar
        public bool IsConnected { get; private set; }

        // NEW: rolling alert queue shown in the overlay panel (newest first)
        private readonly List<string> _alertQueue = new();
        private const int AlertQueueMax = 30;
        public IReadOnlyList<string> AlertQueue => _alertQueue;

        // NEW: fires whenever alert queue or connection state changes
        //      SimulationView subscribes to trigger a lightweight UI refresh
        public event Action? StateChanged;

        // NEW: fires when an emergency aircraft is detected in the snapshot
        public event Action<string>? EmergencyDetected;

        // Tracks which flight IDs we've already raised emergency events for
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

        // Called 60× per second by AirportCanvas
        public double GetInterpolationT()
        {
            if (TargetSnapshot == null || PreviousSnapshot == null) return 0;
            double elapsed = (DateTime.UtcNow - LastSnapshotTime).TotalMilliseconds;
            return Math.Clamp(elapsed / 200.0, 0.0, 1.0);
        }

        // NEW: interpolate a single aircraft's position given its FlightId
        //      Returns null if the aircraft isn't in both snapshots
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

            // NEW: sync server-side alerts into our local queue
            foreach (var alert in snapshot.RecentAlerts)
                if (!_alertQueue.Contains(alert))
                    PushAlert(alert);

            // NEW: detect any emergency aircraft and raise event on UI thread
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

            // Clean up finished emergencies
            var activeIds = snapshot.ActiveAircraft.Select(a => a.FlightId).ToHashSet();
            _knownEmergencies.IntersectWith(activeIds);
        }

        private void HandleAlert(string message)
        {
            PushAlert(message);
        }

        private void SetConnected(bool connected)
        {
            IsConnected = connected;
            if (!connected)
                PushAlert("⚠ Disconnected from server — reconnecting...");

            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        private void PushAlert(string message)
        {
            // Prepend so newest is at index 0
            _alertQueue.Insert(0, message);
            if (_alertQueue.Count > AlertQueueMax)
                _alertQueue.RemoveAt(_alertQueue.Count - 1);

            Dispatcher.UIThread.Post(() => StateChanged?.Invoke());
        }

        // ── Convenience properties for the status bar ─────────────────────────

        // NEW: formatted sim time string
        public string SimTimeText => TargetSnapshot != null
            ? $"{TargetSnapshot.SimulatedTime:HH:mm:ss}"
            : "--:--:--";

        // NEW: formatted speed string
        public string SpeedText => TargetSnapshot != null
            ? $"{TargetSnapshot.TimeScale}×"
            : "–";

        // NEW: weather icon + name
        public string WeatherText => TargetSnapshot != null
            ? WeatherIcon(TargetSnapshot.Weather)
            : "";

        // NEW: true when any active aircraft is squawking emergency
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