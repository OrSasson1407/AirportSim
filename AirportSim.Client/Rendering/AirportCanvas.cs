using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AirportSim.Client.ViewModels;

namespace AirportSim.Client.Rendering
{
    public class AirportCanvas : Control
    {
        private SimulationViewModel? _viewModel;
        private DispatcherTimer? _renderTimer;

        // All our renderers
        private readonly SkyRenderer _skyRenderer = new();
        private readonly GroundRenderer _groundRenderer = new();
        private readonly RunwayRenderer _runwayRenderer = new();
        private readonly AircraftRenderer _aircraftRenderer = new();

        public void Initialize(SimulationViewModel viewModel)
        {
            _viewModel = viewModel;

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += delegate { InvalidateVisual(); };
            _renderTimer.Start();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (_viewModel?.TargetSnapshot == null)
            {
                context.FillRectangle(Brushes.Black, Bounds);
                return;
            }

            double t = _viewModel.GetInterpolationT(); // Calculate smooth time variable

            double scaleX = Bounds.Width / 2000.0;
            double scaleY = Bounds.Height / 600.0;

            using (context.PushTransform(Matrix.CreateScale(scaleX, scaleY)))
            {
                // Draw everything back-to-front
                _skyRenderer.Render(context, _viewModel.TargetSnapshot.SimulatedTime);
                _groundRenderer.Render(context);
                
                // FIXED: Passing the SimulatedTime to the runway renderer
                _runwayRenderer.Render(context, _viewModel.TargetSnapshot.SimulatedTime);
                
                // Draw the planes on top with interpolation
                _aircraftRenderer.Render(context, _viewModel.PreviousSnapshot, _viewModel.TargetSnapshot, t); 
            }
        }
    }
}