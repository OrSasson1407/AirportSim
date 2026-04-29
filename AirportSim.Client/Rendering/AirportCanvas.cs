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

        // NEW: emergency flash overlay state
        private bool   _emergencyFlash;
        private double _emergencyFlashAccumMs;
        private const double EmergencyFlashIntervalMs = 400;

        public void Initialize(SimulationViewModel viewModel)
        {
            _viewModel = viewModel;

            // NEW: trigger extra invalidation when alerts/state change
            _viewModel.StateChanged       += () => Dispatcher.UIThread.Post(InvalidateVisual);
            _viewModel.EmergencyDetected  += _ => _emergencyFlash = true;

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 fps
            };
            _renderTimer.Tick += delegate { InvalidateVisual(); };
            _renderTimer.Start();
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);

            // Track real elapsed time for blink timers
            DateTime now          = DateTime.UtcNow;
            double   realDeltaMs  = (now - _lastRenderTime).TotalMilliseconds;
            _lastRenderTime       = now;

            // ── No data yet ───────────────────────────────────────────────────
            if (_viewModel?.TargetSnapshot == null)
            {
                ctx.FillRectangle(Brushes.Black, Bounds);
                DrawConnectingMessage(ctx);
                return;
            }

            var snap    = _viewModel.TargetSnapshot;
            var weather = snap.Weather;
            double t    = _viewModel.GetInterpolationT();

            double scaleX = Bounds.Width  / 2000.0;
            double scaleY = Bounds.Height / 600.0;

            using (ctx.PushTransform(Matrix.CreateScale(scaleX, scaleY)))
            {
                // Draw layers back-to-front
                _sky.Render(ctx, snap.SimulatedTime, weather);
                _ground.Render(ctx, snap.SimulatedTime, weather);
                _runway.Render(ctx, snap.SimulatedTime, weather, realDeltaMs);
                _aircraft.Render(ctx, _viewModel.PreviousSnapshot, snap, t);
            }

            // ── Emergency flash overlay (drawn in screen space, not world space)
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

                    // Red border pulse
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