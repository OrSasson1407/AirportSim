namespace AirportSim.Client.ViewModels
{
    public class MainViewModel
    {
        public SimulationViewModel Simulation { get; }

        public MainViewModel()
        {
            Simulation = new SimulationViewModel();
        }
    }
}