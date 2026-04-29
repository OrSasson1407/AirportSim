using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
        private bool                 _dashboardVisible;

        public SimulationView()
        {
            InitializeComponent();
        }

        public void Initialize(SimulationViewModel vm)
        {
            _vm = vm;

            var canvas = this.FindControl<AirportSim.Client.Rendering.AirportCanvas>("SimCanvas");
            canvas?.Initialize(vm);

            vm.StateChanged      += RefreshAlertList;
            vm.EmergencyDetected += OnEmergencyDetected;

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

            var dot = this.FindControl<Border>("ConnectionIndicator");
            if (dot != null)
                dot.Background = _vm.IsConnected
                    ? new SolidColorBrush(Color.FromRgb(60, 200, 80))
                    : new SolidColorBrush(Color.FromRgb(200, 60, 60));

            if (snap == null) return;

            var timeText = this.FindControl<TextBlock>("TimeText");
            if (timeText != null)
            {
                string pause = snap.IsPaused ? " ⏸" : "";
                timeText.Text = $"{snap.SimulatedTime:HH:mm}  {snap.TimeScale}×{pause}";
            }

            var runwayText = this.FindControl<TextBlock>("RunwayText");
            if (runwayText != null)
            {
                if (snap.Runways.Count >= 2)
                {
                    var arr = snap.Runways[0];
                    var dep = snap.Runways[1];
                    bool arrOcc = arr.Status == RunwayStatus.Occupied;
                    bool depOcc = dep.Status == RunwayStatus.Occupied;
                    runwayText.Text = $"{(arrOcc ? "28L ▐" : "28L ○")}  {(depOcc ? "28R ▐" : "28R ○")}";
                    runwayText.Foreground = (arrOcc || depOcc)
                        ? new SolidColorBrush(Color.FromRgb(220, 80, 60))
                        : new SolidColorBrush(Color.FromRgb(80, 200, 100));
                }
                else
                {
                    bool occupied = snap.RunwayStatus == RunwayStatus.Occupied;
                    runwayText.Text       = occupied ? "RWY ▐" : "RWY ○";
                    runwayText.Foreground = occupied
                        ? new SolidColorBrush(Color.FromRgb(220, 80, 60))
                        : new SolidColorBrush(Color.FromRgb(80, 200, 100));
                }
            }

            var weatherText = this.FindControl<TextBlock>("WeatherText");
            if (weatherText != null) weatherText.Text = _vm.WeatherText;

            var statsText = this.FindControl<TextBlock>("StatsText");
            if (statsText != null)
                statsText.Text =
                    $"✈ {snap.ActiveAircraft.Count}  " +
                    $"↓{snap.TotalArrivalsToday}  " +
                    $"↑{snap.TotalDeparturesToay}  " +
                    $"↩{snap.GoAroundsToday}";

            var pauseBtn = this.FindControl<Button>("PauseButton");
            if (pauseBtn != null)
                pauseBtn.Content = snap.IsPaused ? "▶ Resume" : "⏸ Pause";

            UpdateQueuePanel(snap);

            if (_dashboardVisible)
                UpdateDashboard();
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
            if (list != null) list.ItemsSource = _vm.AlertQueue.ToList();

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

            var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            hideTimer.Tick += (_, _) =>
            {
                if (_vm?.HasActiveEmergency != true) banner.IsVisible = false;
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

        private void ToggleDashboard_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _dashboardVisible = !_dashboardVisible;
            var panel = this.FindControl<Border>("DashboardPanel");
            var btn   = this.FindControl<Button>("ToggleDashboardButton");
            if (panel != null) panel.IsVisible = _dashboardVisible;
            if (btn   != null)
                btn.Foreground = _dashboardVisible
                    ? new SolidColorBrush(Color.FromRgb(100, 200, 255))
                    : new SolidColorBrush(Color.FromArgb(136, 204, 221, 255));
            if (_dashboardVisible) UpdateDashboard();
        }

        private void UpdateDashboard()
        {
            if (_vm == null) return;

            // ── Traffic graph ─────────────────────────────────────────────────
            var canvas = this.FindControl<Canvas>("TrafficGraphCanvas");
            if (canvas != null)
            {
                canvas.Children.Clear();
                var history = _vm.TrafficHistory;
                int    n    = history.Count;
                double cw   = canvas.Bounds.Width  > 0 ? canvas.Bounds.Width  : 300;
                double ch   = canvas.Bounds.Height > 0 ? canvas.Bounds.Height : 60;
                int maxVal  = 1;
                for (int i = 0; i < n; i++)
                    if (history[i].Count > maxVal) maxVal = history[i].Count;

                double barW = n > 0 ? cw / Math.Max(n, 1) - 2 : cw;
                for (int i = 0; i < n; i++)
                {
                    double fraction = maxVal > 0 ? (double)history[i].Count / maxVal : 0;
                    double barH     = Math.Max(2, fraction * (ch - 4));
                    double bx       = i * (cw / Math.Max(n, 1));
                    double by       = ch - barH - 2;

                    byte r  = (byte)(fraction > 0.7 ? 220 : fraction > 0.4 ? 200 : 60);
                    byte g2 = (byte)(fraction > 0.7 ? 100 : fraction > 0.4 ? 170 : 180);

                    var rect = new Rectangle
                    {
                        Width   = Math.Max(barW, 2),
                        Height  = barH,
                        Fill    = new SolidColorBrush(Color.FromArgb(200, r, g2, 60)),
                        RadiusX = 2,
                        RadiusY = 2
                    };
                    Canvas.SetLeft(rect, bx + 1);
                    Canvas.SetTop(rect, by);
                    canvas.Children.Add(rect);
                }

                if (n > 0)
                {
                    var lbl = new Avalonia.Controls.TextBlock
                    {
                        Text       = $"{history[^1].Count} ac",
                        FontSize   = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(180, 200, 220, 255))
                    };
                    Canvas.SetRight(lbl, 2);
                    Canvas.SetTop(lbl, 2);
                    canvas.Children.Add(lbl);
                }
            }

            // ── Go-arounds by weather ─────────────────────────────────────────
            var goList = this.FindControl<ItemsControl>("GoAroundList");
            if (goList != null)
            {
                var rows = _vm.GoAroundsByWeather
                    .Select(kv => new
                    {
                        Label = WeatherLabel(kv.Key),
                        Value = $"{kv.Value,4}"
                    })
                    .ToList();
                goList.ItemsSource = rows;
            }

            // ── Runway utilisation bars ───────────────────────────────────────
            UpdateUtilBar("Rwy0Bar", _vm.Runway0Utilisation);
            UpdateUtilBar("Rwy1Bar", _vm.Runway1Utilisation);
        }

        private void UpdateUtilBar(string barName, double fraction)
        {
            var bar    = this.FindControl<Border>(barName);
            var parent = bar?.Parent as Border;
            if (bar == null || parent == null) return;

            double maxW = parent.Bounds.Width > 0
                ? parent.Bounds.Width - parent.Padding.Left - parent.Padding.Right
                : 300;
            bar.Width = Math.Clamp(fraction * maxW, 0, maxW);

            bar.Background = fraction < 0.6
                ? new SolidColorBrush(Color.FromRgb(76,  175, 80))
                : fraction < 0.85
                    ? new SolidColorBrush(Color.FromRgb(255, 180, 30))
                    : new SolidColorBrush(Color.FromRgb(220, 60,  60));
        }

        private static string WeatherLabel(WeatherCondition w) => w switch
        {
            WeatherCondition.Clear  => "☀  Clear ",
            WeatherCondition.Cloudy => "⛅ Cloudy",
            WeatherCondition.Rain   => "🌧 Rain  ",
            WeatherCondition.Fog    => "🌫 Fog   ",
            WeatherCondition.Storm  => "⛈ Storm ",
            _                       => w.ToString()
        };

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