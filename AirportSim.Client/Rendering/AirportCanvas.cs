using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AirportSim.Client.ViewModels;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Rendering
{
    public class AirportCanvas : Control
    {
        private SimulationViewModel? _viewModel;
        private DispatcherTimer?     _renderTimer;
        private DateTime             _lastRenderTime = DateTime.UtcNow;

        // Renderers
        private readonly SkyRenderer      _sky      = new();
        private readonly GroundRenderer   _ground   = new();
        private readonly RunwayRenderer   _runway   = new();
        private readonly AircraftRenderer _aircraft = new();
        private readonly RadarRenderer    _radar    = new();

        // Emergency flash overlay state
        private bool   _emergencyFlash;
        private double _emergencyFlashAccumMs;
        private const double EmergencyFlashIntervalMs = 400;

        // Radar toggle — press R or click the radar button
        private bool _radarVisible = false;

        public bool RadarVisible
        {
            get => _radarVisible;
            set { _radarVisible = value; InvalidateVisual(); }
        }

        public void Initialize(SimulationViewModel viewModel)
        {
            _viewModel = viewModel;

            _viewModel.StateChanged      += () => Dispatcher.UIThread.Post(InvalidateVisual);
            _viewModel.EmergencyDetected += _ => _emergencyFlash = true;

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += delegate { InvalidateVisual(); };
            _renderTimer.Start();
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);

            DateTime now         = DateTime.UtcNow;
            double   realDeltaMs = (now - _lastRenderTime).TotalMilliseconds;
            _lastRenderTime      = now;

            // ── No data yet ───────────────────────────────────────────────────
            if (_viewModel?.TargetSnapshot == null)
            {
                ctx.FillRectangle(Brushes.Black, Bounds);
                DrawConnectingMessage(ctx);
                return;
            }

            var    snap    = _viewModel.TargetSnapshot;
            var    weather = snap.Weather;
            double t       = _viewModel.GetInterpolationT();

            double scaleX = Bounds.Width  / 2000.0;
            double scaleY = Bounds.Height / 600.0;

            // ── Main world view ───────────────────────────────────────────────
            using (ctx.PushTransform(Matrix.CreateScale(scaleX, scaleY)))
            {
                _sky.Render(ctx, snap.SimulatedTime, weather);
                _ground.Render(ctx, snap.SimulatedTime, weather, snap.ActiveAircraft, realDeltaMs);
                _runway.Render(ctx, snap.SimulatedTime, weather, realDeltaMs);
                _aircraft.Render(ctx, _viewModel.PreviousSnapshot, snap, t);
            }

            // ── Radar overlay (screen-space, top-right corner) ────────────────
            if (_radarVisible)
            {
                double radarSize = Math.Min(Bounds.Width * 0.28, 240);
                double margin    = 12;
                double rx        = Bounds.Width  - radarSize - margin;
                double ry        = margin;

                _radar.Render(ctx,
                    _viewModel.PreviousSnapshot,
                    snap,
                    t,
                    new Rect(rx, ry, radarSize, radarSize));
            }

            // ── Emergency flash overlay ───────────────────────────────────────
            if (_viewModel.HasActiveEmergency)
            {
                _emergencyFlashAccumMs += realDeltaMs;
                if (_emergencyFlashAccumMs >= EmergencyFlashIntervalMs)
                {
                    _emergencyFlash        = !_emergencyFlash;
                    _emergencyFlashAccumMs = 0;
                }

                if (_emergencyFlash)
                {
                    ctx.FillRectangle(
                        new SolidColorBrush(Color.FromArgb(30, 220, 30, 30)),
                        Bounds);
                    ctx.DrawRectangle(
                        null,
                        new Pen(new SolidColorBrush(Color.FromArgb(160, 220, 40, 40)), 4),
                        Bounds);
                }
            }
            else
            {
                _emergencyFlash        = false;
                _emergencyFlashAccumMs = 0;
            }
        }

        private void DrawConnectingMessage(DrawingContext ctx)
        {
            var text = new FormattedText(
                "Connecting to simulation server...",
                System.Globalization.CultureInfo.CurrentCulture,
                Avalonia.Media.FlowDirection.LeftToRight,
                new Typeface("Arial"),
                18,
                Brushes.Gray);

            ctx.DrawText(text, new Point(
                Bounds.Width  / 2 - text.Width  / 2,
                Bounds.Height / 2 - text.Height / 2));
        }
    }
}