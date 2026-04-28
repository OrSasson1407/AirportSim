using Avalonia.Controls;
using Avalonia.Threading;
using AirportSim.Client.ViewModels;
using System;

namespace AirportSim.Client.Views
{
    public partial class SimulationView : UserControl
    {
        private SimulationViewModel? _viewModel;
        private DispatcherTimer? _uiTimer;
        private bool _isPaused = false;

        public SimulationView()
        {
            InitializeComponent();
        }

        public void Initialize(SimulationViewModel viewModel)
        {
            _viewModel = viewModel;
            
            // Start the SignalR connection
            _viewModel.Start();

            // Link the canvas to the ViewModel
            var canvas = this.FindControl<AirportSim.Client.Rendering.AirportCanvas>("SimCanvas");
            canvas?.Initialize(viewModel);

            // Simple UI updater for the bottom bar (updates twice a second)
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += delegate { UpdateUI(); };
            _uiTimer.Start();
        }

        private void UpdateUI()
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            var timeText = this.FindControl<TextBlock>("TimeText");

            if (_viewModel?.TargetSnapshot != null && statusText != null && timeText != null)
            {
                var snap = _viewModel.TargetSnapshot;
                statusText.Text = $"Connected | Runway: {snap.RunwayStatus} | Planes: {snap.ActiveAircraft.Count} | Queue: {snap.QueuedFlights.Count}";
                timeText.Text = $"Sim Time: {snap.SimulatedTime:HH:mm:ss} ({snap.TimeScale}x)";
            }
        }

        private void Speed1x_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _viewModel?.Connection.SetTimeScaleAsync(1.0);
        }

        private void Speed60x_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _viewModel?.Connection.SetTimeScaleAsync(60.0);
        }

        private void Pause_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            _viewModel?.Connection.SetPausedAsync(_isPaused);
        }
    }
}