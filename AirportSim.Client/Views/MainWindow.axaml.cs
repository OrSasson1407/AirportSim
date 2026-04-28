using Avalonia.Controls;
using AirportSim.Client.ViewModels; // <-- This is the line that fixes the error!

namespace AirportSim.Client.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize the top-level state
            _viewModel = new MainViewModel();
            
            // Find the SimulationView in the UI and boot it up
            var simView = this.FindControl<SimulationView>("MainSimView");
            simView?.Initialize(_viewModel.Simulation);
        }
    }
}