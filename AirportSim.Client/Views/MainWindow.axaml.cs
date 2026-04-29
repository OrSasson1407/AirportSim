using Avalonia.Controls;
using Avalonia.Threading;
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

            // Dismiss splash screen after 1.5 seconds to reveal the UI
            DispatcherTimer.RunOnce(() =>
            {
                var splash = this.FindControl<Border>("SplashOverlay");
                if (splash != null) splash.IsVisible = false;
            }, TimeSpan.FromSeconds(1.5));
        }
    }
}