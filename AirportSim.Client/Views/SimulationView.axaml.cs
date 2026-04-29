using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AirportSim.Client.ViewModels;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Views
{
    public partial class SimulationView : UserControl
    {
        private SimulationViewModel? _vm;
        private DispatcherTimer?     _uiTimer;
        private bool                 _isPaused;
        private bool                 _queueVisible;
        private bool                 _alertsVisible;
        private bool                 _radarVisible;

        public SimulationView()
        {
            InitializeComponent();
        }

        public void Initialize(SimulationViewModel vm)
        {
            _vm = vm;

            // Wire canvas
            var canvas = this.FindControl<AirportSim.Client.Rendering.AirportCanvas>("SimCanvas");
            canvas?.Initialize(vm);

            // Subscribe to state changes
            vm.StateChanged      += RefreshAlertList;
            vm.EmergencyDetected += OnEmergencyDetected;

            // Lightweight UI refresh at 2 Hz for the status bar
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += delegate { UpdateStatusBar(); };
            _uiTimer.Start();

            vm.Start();
        }

        // ── Status bar ────────────────────────────────────────────────────────

        private void UpdateStatusBar()
        {
            if (_vm == null) return;
            var snap = _vm.TargetSnapshot;

            // Connection indicator dot
            var dot = this.FindControl<Border>("ConnectionIndicator");
            if (dot != null)
                dot.Background = _vm.IsConnected
                    ? new SolidColorBrush(Color.FromRgb(60, 200, 80))
                    : new SolidColorBrush(Color.FromRgb(200, 60, 60));

            if (snap == null) return;

            // Sim time + speed
            var timeText = this.FindControl<TextBlock>("TimeText");
            if (timeText != null)
            {
                string pause = snap.IsPaused ? " ⏸" : "";
                timeText.Text = $"{snap.SimulatedTime:HH:mm}  {snap.TimeScale}×{pause}";
            }

            // Runway status — now shows both runways
            var runwayText = this.FindControl<TextBlock>("RunwayText");
            if (runwayText != null)
            {
                if (snap.Runways.Count >= 2)
                {
                    var arr = snap.Runways[0];
                    var dep = snap.Runways[1];

                    bool arrOcc = arr.Status == RunwayStatus.Occupied;
                    bool depOcc = dep.Status == RunwayStatus.Occupied;

                    string arrStr = arrOcc ? "28L ▐" : "28L ○";
                    string depStr = depOcc ? "28R ▐" : "28R ○";

                    runwayText.Text = $"{arrStr}  {depStr}";

                    // Colour red if either occupied, green if both free
                    runwayText.Foreground = (arrOcc || depOcc)
                        ? new SolidColorBrush(Color.FromRgb(220, 80, 60))
                        : new SolidColorBrush(Color.FromRgb(80, 200, 100));
                }
                else
                {
                    // Legacy fallback
                    bool occupied = snap.RunwayStatus == RunwayStatus.Occupied;
                    runwayText.Text       = occupied ? "RWY ▐" : "RWY ○";
                    runwayText.Foreground = occupied
                        ? new SolidColorBrush(Color.FromRgb(220, 80, 60))
                        : new SolidColorBrush(Color.FromRgb(80, 200, 100));
                }
            }

            // Weather
            var weatherText = this.FindControl<TextBlock>("WeatherText");
            if (weatherText != null)
                weatherText.Text = _vm.WeatherText;

            // Stats — planes / arrivals / go-arounds
            var statsText = this.FindControl<TextBlock>("StatsText");
            if (statsText != null)
                statsText.Text =
                    $"✈ {snap.ActiveAircraft.Count}  " +
                    $"↓{snap.TotalArrivalsToday}  " +
                    $"↑{snap.TotalDeparturesToay}  " +
                    $"↩{snap.GoAroundsToday}";

            // Pause button label
            var pauseBtn = this.FindControl<Button>("PauseButton");
            if (pauseBtn != null)
                pauseBtn.Content = snap.IsPaused ? "▶ Resume" : "⏸ Pause";

            // Queue panel
            UpdateQueuePanel(snap);
        }

        private void UpdateQueuePanel(SimSnapshot snap)
        {
            var list = this.FindControl<ItemsControl>("QueueList");
            if (list == null) return;

            var lines = snap.QueuedFlights.Select(f =>
            {
                string arrow = f.FlightType == FlightType.Arrival ? "↓" : "↑";
                string type  = f.Type.ToString()[0].ToString();
                string delay = f.DelayMinutes > 0 ? $" +{f.DelayMinutes}m" : "";
                string route = f.FlightType == FlightType.Arrival
                    ? $"{f.Origin}→TLV"
                    : $"TLV→{f.Destination}";
                return $"{arrow}{type}  {f.FlightId,-12} {route}{delay}";
            }).ToList();

            list.ItemsSource = lines;
        }

        // ── Alert panel ───────────────────────────────────────────────────────

        private void RefreshAlertList()
        {
            if (_vm == null) return;

            var list = this.FindControl<ItemsControl>("AlertList");
            if (list != null)
                list.ItemsSource = _vm.AlertQueue.ToList();

            var scroller = this.FindControl<ScrollViewer>("AlertScroller");
            scroller?.ScrollToHome();

            var btn = this.FindControl<Button>("ToggleAlertsButton");
            if (btn != null && _vm.AlertQueue.Count > 0 && !_alertsVisible)
                btn.Content = $"🔔 Alerts ({_vm.AlertQueue.Count})";
        }

        // ── Emergency banner ──────────────────────────────────────────────────

        private void OnEmergencyDetected(string flightId)
        {
            var banner = this.FindControl<Border>("EmergencyBanner");
            var text   = this.FindControl<TextBlock>("EmergencyText");

            if (banner == null || text == null) return;

            text.Text        = $"🚨 EMERGENCY  {flightId}  —  MAYDAY";
            banner.IsVisible = true;

            var hideTimer = new DispatcherTimer
                { Interval = TimeSpan.FromSeconds(8) };
            hideTimer.Tick += (_, _) =>
            {
                if (_vm?.HasActiveEmergency != true)
                    banner.IsVisible = false;
                hideTimer.Stop();
            };
            hideTimer.Start();
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void Pause_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            _vm?.Connection.SetPausedAsync(_isPaused);
        }

        private void Speed1x_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            _vm?.Connection.SetTimeScaleAsync(1.0);

        private void Speed60x_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            _vm?.Connection.SetTimeScaleAsync(60.0);

        private void SpeedUp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            _vm?.Connection.StepSpeedUpAsync();

        private void SpeedDown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            _vm?.Connection.StepSpeedDownAsync();

        private void Emergency_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            _vm?.Connection.DeclareEmergencyAsync();

        private void Weather_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
            _vm?.Connection.CycleWeatherAsync();

        private void ToggleQueue_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _queueVisible = !_queueVisible;
            var panel = this.FindControl<Border>("QueuePanel");
            var btn   = this.FindControl<Button>("ToggleQueueButton");
            if (panel != null) panel.IsVisible = _queueVisible;
            if (btn   != null) btn.Foreground  = _queueVisible
                ? new SolidColorBrush(Color.FromRgb(100, 220, 120))
                : new SolidColorBrush(Color.FromArgb(136, 204, 255, 170));
        }

        private void ToggleAlerts_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _alertsVisible = !_alertsVisible;
            var panel = this.FindControl<Border>("AlertPanel");
            var btn   = this.FindControl<Button>("ToggleAlertsButton");
            if (panel != null) panel.IsVisible = _alertsVisible;
            if (btn   != null)
            {
                btn.Content    = _alertsVisible ? "🔔 Alerts" : $"🔔 Alerts ({_vm?.AlertQueue.Count ?? 0})";
                btn.Foreground = _alertsVisible
                    ? new SolidColorBrush(Color.FromRgb(120, 140, 255))
                    : new SolidColorBrush(Color.FromArgb(136, 170, 187, 255));
            }
        }

        private void ClearAlerts_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var list = this.FindControl<ItemsControl>("AlertList");
            if (list != null) list.ItemsSource = new List<string>();
        }

        private void ToggleRadar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _radarVisible = !_radarVisible;

            var canvas = this.FindControl<AirportSim.Client.Rendering.AirportCanvas>("SimCanvas");
            if (canvas != null) canvas.RadarVisible = _radarVisible;

            var btn = this.FindControl<Button>("ToggleRadarButton");
            if (btn != null)
                btn.Foreground = _radarVisible
                    ? new SolidColorBrush(Color.FromRgb(0, 220, 120))
                    : new SolidColorBrush(Color.FromArgb(136, 170, 255, 204));
        }
    }
}