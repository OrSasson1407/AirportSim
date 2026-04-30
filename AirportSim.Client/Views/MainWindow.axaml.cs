using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using AirportSim.Client.ViewModels;

namespace AirportSim.Client.Views
{
    public partial class MainWindow : Window
    {
        // NEW: public so App.axaml.cs can access it for graceful shutdown
        public MainViewModel? ViewModel { get; private set; }

        public MainWindow()
        {
            InitializeComponent();

            ViewModel = new MainViewModel();

            var simView = this.FindControl<SimulationView>("MainSimView");
            simView?.Initialize(ViewModel.Simulation);

            // The splash screen is now dismissed manually by the StartSim_Click handler
        }

        private void StartSim_Click(object? sender, RoutedEventArgs e)
        {
            // 1. Hide the splash overlay
            var splash = this.FindControl<Border>("SplashOverlay");
            if (splash != null) splash.IsVisible = false;

            // 2. Determine which airport was selected
            var combo = this.FindControl<ComboBox>("AirportSelector");
            string? selection = (combo?.SelectedItem as ComboBoxItem)?.Content?.ToString();

            string layoutId = "tlv"; // Default
            if (!string.IsNullOrEmpty(selection))
            {
                if (selection.Contains("LHR")) layoutId = "lhr";
                else if (selection.Contains("JFK")) layoutId = "jfk";
            }

            // 3. Connect to the server
            ViewModel?.Simulation.Start();

            // Note: We'll need to send this layout choice to the Server!
            // E.g., await ViewModel.Simulation.Connection.SetAirportLayoutAsync(layoutId);
            // We will wire up that SignalR command in the backend next.
        }
    }
}