using System.Collections.Generic;
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
        Task ReceiveAudioTriggers(List<string> audioFiles);
    }

    public class SimulationHub : Hub<ISimulationClient>
    {
        private readonly SimulationEngine _engine;

        public SimulationHub(SimulationEngine engine)
        {
            _engine = engine;
        }

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

        public async Task DeclareEmergency()
        {
            _engine.InjectEmergency();
            await Clients.All.ReceiveAlert("🚨 MAYDAY declared — emergency aircraft inbound");
        }

        public async Task CycleWeather()
        {
            var next = _engine.CycleWeather();
            await Clients.All.ReceiveAlert($"🌤 Weather changed to {next}");
        }

        public async Task SetAirportLayout(string layoutId)
        {
            _engine.LoadLayout(layoutId); 
            await Clients.All.ReceiveAlert($"🌍 Airport layout changed to {layoutId.ToUpper()}");
        }

        // NEW: Allow UI to override the RVR
        public Task SetRvr(int rvrMeters)
        {
            _engine.SetRvr(rvrMeters);
            return Task.CompletedTask;
        }
    }
}