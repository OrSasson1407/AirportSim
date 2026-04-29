using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
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

        // Design-system colours (match App.axaml tokens)
        private static readonly SolidColorBrush BrushSuccess    = new(Color.FromRgb(34,  197, 94));
        private static readonly SolidColorBrush BrushPrimary    = new(Color.FromRgb(0,   217, 245));
        private static readonly SolidColorBrush BrushWarning    = new(Color.FromRgb(245, 158, 11));
        private static readonly SolidColorBrush BrushDanger     = new(Color.FromRgb(239, 68,  68));
        private static readonly SolidColorBrush BrushInfo       = new(Color.FromRgb(59,  130, 246));
        private static readonly SolidColorBrush BrushTextMuted  = new(Color.FromRgb(90,  106, 122));
        private static readonly SolidColorBrush BrushText       = new(Color.FromRgb(232, 237, 245));
        private static readonly SolidColorBrush BrushGray       = new(Color.FromRgb(100, 116, 132));
        // Accent brush referenced in toggle handlers
        private static readonly SolidColorBrush BrushAccent     = new(Color.FromRgb(255, 176, 32));

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

        // ── Lifecycle & Cleanup ───────────────────────────────────────────────
        
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // 1. Stop the UI timer to prevent background ticking
            if (_uiTimer != null)
            {
                _uiTimer.Stop();
                _uiTimer = null;
            }

            // 2. Unsubscribe from ViewModel events to prevent memory leaks
            if (_vm != null)
            {
                _vm.StateChanged -= RefreshAlertList;
                _vm.EmergencyDetected -= OnEmergencyDetected;
            }
        }

        // ── Status bar ────────────────────────────────────────────────────────

        private void UpdateStatusBar()
        {
            if (_vm == null) return;
            var snap = _vm.TargetSnapshot;

            // Connection dot (now in status bar)
            var dot = this.FindControl<Border>("ConnectionIndicator");
            if (dot != null)
                dot.Background = _vm.IsConnected ? BrushSuccess : BrushDanger;

            if (snap == null) return;

            // Time display in sidebar widget
            var timeText = this.FindControl<TextBlock>("TimeText");
            if (timeText != null)
            {
                string pause = snap.IsPaused ? "  ⏸" : "";
                timeText.Text = $"{snap.SimulatedTime:HH:mm}  {snap.TimeScale}×{pause}";
            }

            // Pause button label
            var pauseBtn = this.FindControl<Button>("PauseButton");
            if (pauseBtn != null)
                pauseBtn.Content = snap.IsPaused ? "▶  RESUME SIMULATION" : "⏸  PAUSE SIMULATION";

            // Weather label in sidebar
            var weatherText = this.FindControl<TextBlock>("WeatherText");
            if (weatherText != null) weatherText.Text = _vm.WeatherText;

            // ── Runway chips ──────────────────────────────────────────────────
            if (snap.Runways.Count >= 2)
            {
                UpdateRunwayChip(
                    snap.Runways[0].Status == RunwayStatus.Occupied,
                    "Rwy28LDot", "Rwy28LStatus",
                    BrushSuccess, BrushDanger);

                UpdateRunwayChip(
                    snap.Runways[1].Status == RunwayStatus.Occupied,
                    "Rwy28RDot", "Rwy28RStatus",
                    BrushPrimary, BrushDanger);
            }
            else if (snap.Runways.Count == 1)
            {
                bool occ = snap.RunwayStatus == RunwayStatus.Occupied;
                UpdateRunwayChip(occ, "Rwy28LDot", "Rwy28LStatus", BrushSuccess, BrushDanger);
            }

            // ── Metric chips ──────────────────────────────────────────────────
            SetText("ChipAircraft",   snap.ActiveAircraft.Count.ToString());
            SetText("ChipArrivals",   snap.TotalArrivalsToday.ToString());
            SetText("ChipDepartures", snap.TotalDeparturesToay.ToString());
            SetText("ChipGoArounds",  snap.GoAroundsToday.ToString());

            // Legacy targets (kept for backwards compat, now hidden)
            SetText("RunwayText", "");
            SetText("StatsText",  "");

            UpdateQueuePanel(snap);
            UpdateAircraftInfoPanel();

            if (_dashboardVisible)
                UpdateDashboard();
        }

        private void UpdateRunwayChip(bool occupied, string dotName, string labelName,
                                       SolidColorBrush clearBrush, SolidColorBrush occBrush)
        {
            var dot   = this.FindControl<Border>(dotName);
            var label = this.FindControl<TextBlock>(labelName);
            if (dot != null)   dot.Background = occupied ? occBrush : clearBrush;
            if (label != null)
            {
                label.Text       = occupied ? "OCC" : "CLEAR";
                label.Foreground = occupied ? occBrush : clearBrush;
            }
        }

        private void SetText(string name, string value)
        {
            var tb = this.FindControl<TextBlock>(name);
            if (tb != null) tb.Text = value;
        }

        // ── Aircraft info popover ─────────────────────────────────────────────

        private void UpdateAircraftInfoPanel()
        {
            var panel = this.FindControl<Border>("AircraftInfoPanel");
            if (panel == null) return;

            if (_vm?.SelectedAircraft != null)
            {
                panel.IsVisible = true;
                var ac = _vm.SelectedAircraft;

                SetText("InfoFlightId", ac.FlightId);
                SetText("InfoType",     ac.Type.ToString());
                SetText("InfoPhase",    ac.Phase.ToString().ToUpper());
                SetText("InfoAlt",      $"{ac.AltitudeFt:N0} ft");
                SetText("InfoSpeed",    $"{ac.SpeedKts:N0} kts");
                SetText("InfoGate",     string.IsNullOrEmpty(ac.AssignedGate) ? "—" : ac.AssignedGate);

                // Color the phase pill by phase group
                var pill = this.FindControl<Border>("InfoPhasePill");
                var pillText = this.FindControl<TextBlock>("InfoPhase");
                if (pill != null && pillText != null)
                {
                    var (bg, border, fg) = ac.Phase.ToString() switch
                    {
                        var p when p.Contains("Approach") || p.Contains("Landing") || p.Contains("Arrival")
                            => (BrushSuccess, BrushSuccess, BrushSuccess),
                        var p when p.Contains("Taxi") || p.Contains("Gate") || p.Contains("Boarding")
                            => (BrushWarning, BrushWarning, BrushWarning),
                        var p when p.Contains("Takeoff") || p.Contains("Climb") || p.Contains("Depart")
                            => (BrushPrimary, BrushPrimary, BrushPrimary),
                        var p when p.Contains("Emergency") || p.Contains("Mayday")
                            => (BrushDanger, BrushDanger, BrushDanger),
                        _ => (BrushInfo, BrushInfo, BrushInfo)
                    };
                    pill.BorderBrush     = border;
                    pillText.Foreground  = fg;
                }
            }
            else
            {
                panel.IsVisible = false;
            }
        }

        // ── Queue panel ───────────────────────────────────────────────────────

        private void UpdateQueuePanel(SimSnapshot snap)
        {
            var list = this.FindControl<ItemsControl>("QueueList");
            if (list == null) return;

            var lines = snap.QueuedFlights.Select(f =>
            {
                string arrow  = f.FlightType == FlightType.Arrival ? "↓" : "↑";
                string type   = f.Type.ToString()[0].ToString();
                string delay  = f.DelayMinutes > 0 ? $" +{f.DelayMinutes}m" : "";
                string route  = f.FlightType == FlightType.Arrival
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

            // Update badge count
            var badge = this.FindControl<TextBlock>("AlertCountBadge");
            if (badge != null) badge.Text = _vm.AlertQueue.Count.ToString();

            // Flash the button label if panel is closed
            var btn = this.FindControl<Button>("ToggleAlertsButton");
            if (btn != null && _vm.AlertQueue.Count > 0 && !_alertsVisible)
                btn.Foreground = BrushDanger;
            else if (btn != null)
                btn.Foreground = BrushText;
        }

        // ── Emergency banner ──────────────────────────────────────────────────

        private void OnEmergencyDetected(string flightId)
        {
            var banner = this.FindControl<Border>("EmergencyBanner");
            var text   = this.FindControl<TextBlock>("EmergencyText");
            if (banner == null || text == null) return;

            text.Text        = $"🚨  EMERGENCY  {flightId}  —  MAYDAY  MAYDAY";
            banner.IsVisible = true;

            var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
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
            if (btn   != null) btn.Foreground  = _queueVisible ? BrushAccent : BrushText;
        }

        private void ToggleAlerts_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _alertsVisible = !_alertsVisible;
            var panel = this.FindControl<Border>("AlertPanel");
            var btn   = this.FindControl<Button>("ToggleAlertsButton");
            if (panel != null) panel.IsVisible = _alertsVisible;
            if (btn   != null) btn.Foreground  = _alertsVisible ? BrushDanger : BrushText;
        }

        private void ClearAlerts_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var list  = this.FindControl<ItemsControl>("AlertList");
            var badge = this.FindControl<TextBlock>("AlertCountBadge");
            if (list  != null) list.ItemsSource = new List<string>();
            if (badge != null) badge.Text = "0";
        }

        private void ToggleDashboard_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _dashboardVisible = !_dashboardVisible;
            var panel = this.FindControl<Border>("DashboardPanel");
            var btn   = this.FindControl<Button>("ToggleDashboardButton");
            if (panel != null) panel.IsVisible = _dashboardVisible;
            if (btn   != null) btn.Foreground  = _dashboardVisible ? BrushSuccess : BrushText;
            if (_dashboardVisible) UpdateDashboard();
        }

        private void ToggleRadar_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _radarVisible = !_radarVisible;
            var canvas = this.FindControl<AirportSim.Client.Rendering.AirportCanvas>("SimCanvas");
            if (canvas != null) canvas.RadarVisible = _radarVisible;
            var btn = this.FindControl<Button>("ToggleRadarButton");
            if (btn != null) btn.Foreground = _radarVisible ? BrushPrimary : BrushText;
        }

        // ── Dashboard ─────────────────────────────────────────────────────────

        private void UpdateDashboard()
        {
            if (_vm == null) return;

            // ── Traffic graph — line chart with filled area ───────────────────────
            var graphCanvas = this.FindControl<Canvas>("TrafficGraphCanvas");
            if (graphCanvas != null)
            {
                graphCanvas.Children.Clear();
                var history = _vm.TrafficHistory;
                int    n    = history.Count;
                double cw   = graphCanvas.Bounds.Width  > 0 ? graphCanvas.Bounds.Width  : 300;
                double ch   = graphCanvas.Bounds.Height > 0 ? graphCanvas.Bounds.Height : 65;
                int maxVal  = Math.Max(1, n > 0 ? history.Max(h => h.Count) : 1);

                // Subtle horizontal grid lines at 25 / 50 / 75 %
                foreach (double pctLine in new[] { 0.25, 0.5, 0.75 })
                {
                    double gy = ch - pctLine * (ch - 8) - 4;
                    var gridLine = new Line
                    {
                        StartPoint = new Avalonia.Point(0,  gy),
                        EndPoint   = new Avalonia.Point(cw, gy),
                        Stroke     = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                        StrokeThickness = 1
                    };
                    graphCanvas.Children.Add(gridLine);
                }

                if (n >= 2)
                {
                    // Build point list
                    var pts = new List<Avalonia.Point>();
                    for (int i = 0; i < n; i++)
                    {
                        double fraction = (double)history[i].Count / maxVal;
                        double px = i * cw / (n - 1);
                        double py = ch - 4 - fraction * (ch - 8);
                        pts.Add(new Avalonia.Point(px, py));
                    }

                    // Filled area polygon (line pts + bottom corners)
                    var polyPts = new Avalonia.Collections.AvaloniaList<Avalonia.Point>(pts);
                    polyPts.Add(new Avalonia.Point(pts[^1].X, ch));
                    polyPts.Add(new Avalonia.Point(pts[0].X,  ch));

                    var area = new Polygon
                    {
                        Points  = polyPts,
                        Fill    = new SolidColorBrush(Color.FromArgb(40, 0, 217, 245)),
                        Stroke  = Brushes.Transparent,
                    };
                    graphCanvas.Children.Add(area);

                    // Line itself
                    var polyLine = new Polyline
                    {
                        Points          = new Avalonia.Collections.AvaloniaList<Avalonia.Point>(pts),
                        Stroke          = new SolidColorBrush(Color.FromArgb(220, 0, 217, 245)),
                        StrokeThickness = 1.5,
                        StrokeLineCap   = PenLineCap.Round,
                        StrokeJoin      = PenLineJoin.Round,
                    };
                    graphCanvas.Children.Add(polyLine);

                    // Dot at latest point
                    var lastPt = pts[^1];
                    double load = n > 0 ? (double)history[^1].Count / maxVal : 0;
                    Color dotColor = load > 0.75 ? Color.FromRgb(239, 68, 68)
                        : load > 0.45            ? Color.FromRgb(245, 158, 11)
                                                 : Color.FromRgb(0, 217, 245);

                    var dot = new Ellipse
                    {
                        Width  = 7, Height = 7,
                        Fill   = new SolidColorBrush(dotColor),
                    };
                    Canvas.SetLeft(dot, lastPt.X - 3.5);
                    Canvas.SetTop(dot,  lastPt.Y - 3.5);
                    graphCanvas.Children.Add(dot);
                }

                // Peak label top-right
                if (n > 0)
                {
                    var lbl = new TextBlock
                    {
                        Text       = $"peak {maxVal} ac",
                        FontSize   = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(140, 0, 217, 245))
                    };
                    Canvas.SetRight(lbl, 4);
                    Canvas.SetTop(lbl,   3);
                    graphCanvas.Children.Add(lbl);

                    // Current count bottom-right
                    var cur = new TextBlock
                    {
                        Text       = $"now  {history[^1].Count} ac",
                        FontSize   = 9,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.FromArgb(200, 0, 217, 245))
                    };
                    Canvas.SetRight(cur, 4);
                    Canvas.SetBottom(cur, 3);
                    graphCanvas.Children.Add(cur);
                }
            }

            // ── Go-arounds by weather ─────────────────────────────────────────────
            var goList = this.FindControl<ItemsControl>("GoAroundList");
            if (goList != null)
            {
                var rows = _vm.GoAroundsByWeather
                    .Select(kv => new { Label = WeatherLabel(kv.Key), Value = $"{kv.Value,4}" })
                    .ToList();
                goList.ItemsSource = rows;
            }

            // ── Runway utilisation bars + percentage labels ────────────────────────
            UpdateUtilBar("Rwy0Bar", "Rwy0Pct", _vm.Runway0Utilisation);
            UpdateUtilBar("Rwy1Bar", "Rwy1Pct", _vm.Runway1Utilisation);
        }

        private void UpdateUtilBar(string barName, string pctName, double fraction)
        {
            if (this.FindControl<Border>(barName) is not { } bar || 
                bar.Parent is not Border parent || 
                this.FindControl<TextBlock>(pctName) is not { } pct) 
                return;

            double maxW = parent.Bounds.Width > 0
                ? parent.Bounds.Width - parent.Padding.Left - parent.Padding.Right
                : 300;

            bar.Width = Math.Clamp(fraction * maxW, 0, maxW);

            SolidColorBrush barColor = fraction switch
            {
                < 0.60 => BrushSuccess,
                < 0.85 => BrushWarning,
                _      => BrushDanger
            };

            bar.Background = barColor;
            pct.Text       = $"{fraction * 100:F0}%";
            pct.Foreground = barColor;
        }

        private static string WeatherLabel(WeatherCondition w) => w switch
        {
            WeatherCondition.Clear  => "☀  Clear",
            WeatherCondition.Cloudy => "⛅ Cloudy",
            WeatherCondition.Rain   => "🌧 Rain",
            WeatherCondition.Fog    => "🌫 Fog",
            WeatherCondition.Storm  => "⛈ Storm",
            _                       => w.ToString()
        };
    }
}
