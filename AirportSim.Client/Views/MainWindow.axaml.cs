using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using AirportSim.Client.ViewModels;

namespace AirportSim.Client.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel? ViewModel { get; private set; }

        public MainWindow()
        {
            InitializeComponent();

            ViewModel = new MainViewModel();

            var simView = this.FindControl<SimulationView>("MainSimView");
            simView?.Initialize(ViewModel.Simulation);
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

            // 4. Send the layout choice to the server! (Fixes CS0219 warning)
            _ = ViewModel?.Simulation.Connection.SetAirportLayoutAsync(layoutId);
        }
    }
}