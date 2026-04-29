using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using AirportSim.Server.Simulation;
using AirportSim.Shared.Models;

namespace AirportSim.Server.Hubs
{
    public interface ISimulationClient
    {
        Task ReceiveSnapshot(SimSnapshot snapshot);
        Task ReceiveAlert(string message);
    }

    public class SimulationHub : Hub<ISimulationClient>
    {
        private readonly SimulationEngine _engine;

        // SimulationEngine is registered as a singleton so we can inject it here
        public SimulationHub(SimulationEngine engine)
        {
            _engine = engine;
        }

        // ── Client → Server commands ──────────────────────────────────────────

        public async Task SetTimeScale(double scale)
        {
            double applied = _engine.Clock.SetTimeScale(scale);
            await Clients.All.ReceiveAlert($"⏩ Speed set to {applied}x");
        }

        public async Task SetPaused(bool isPaused)
        {
            _engine.Clock.IsPaused = isPaused;
            string status = isPaused ? "⏸ Simulation paused" : "▶ Simulation resumed";
            await Clients.All.ReceiveAlert(status);
        }

        // NEW: step speed up / down (maps to the preset ladder in SimClock)
        public async Task StepSpeedUp()
        {
            double applied = _engine.Clock.StepUp();
            await Clients.All.ReceiveAlert($"⏩ Speed → {applied}x");
        }

        public async Task StepSpeedDown()
        {
            double applied = _engine.Clock.StepDown();
            await Clients.All.ReceiveAlert($"⏪ Speed → {applied}x");
        }

        // NEW: declare an emergency arrival (spawns a priority MAYDAY flight)
        public async Task DeclareEmergency()
        {
            _engine.InjectEmergency();
            await Clients.All.ReceiveAlert("🚨 MAYDAY declared — emergency aircraft inbound");
        }

        // NEW: skip to next weather state (for testing / demo)
        public async Task CycleWeather()
        {
            var next = _engine.CycleWeather();
            await Clients.All.ReceiveAlert($"🌤 Weather changed to {next}");
        }
    }
}