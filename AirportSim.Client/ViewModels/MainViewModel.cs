using AirportSim.Client.Connection;
using AirportSim.Shared.Models;

namespace AirportSim.Client.ViewModels
{
    public class MainViewModel
    {
        public SimulationViewModel Simulation { get; }

        // NEW: expose connection directly for views that need raw send access
        public SimulationConnection Connection => Simulation.Connection;

        public MainViewModel()
        {
            Simulation = new SimulationViewModel();
        }

        // ── Convenience command methods called by the view ────────────────────

        public void Pause()    => Simulation.Connection.SetPausedAsync(true);
        public void Resume()   => Simulation.Connection.SetPausedAsync(false);
        public void SpeedUp()  => Simulation.Connection.StepSpeedUpAsync();
        public void SpeedDown() => Simulation.Connection.StepSpeedDownAsync();

        // NEW: toggle pause based on current sim state
        public void TogglePause()
        {
            bool currentlyPaused = Simulation.TargetSnapshot?.IsPaused ?? false;
            Simulation.Connection.SetPausedAsync(!currentlyPaused);
        }

        // NEW: emergency and weather commands wired through to connection
        public void DeclareEmergency() => Simulation.Connection.DeclareEmergencyAsync();
        public void CycleWeather()     => Simulation.Connection.CycleWeatherAsync();

        // NEW: direct speed presets for the 1× and 60× buttons kept from v1
        public void SetSpeed1x()  => Simulation.Connection.SetTimeScaleAsync(1.0);
        public void SetSpeed60x() => Simulation.Connection.SetTimeScaleAsync(60.0);
    }
}