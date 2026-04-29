using Avalonia.Controls;
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
        }
    }
}