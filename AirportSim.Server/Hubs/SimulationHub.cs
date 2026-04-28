using Microsoft.AspNetCore.SignalR;
using AirportSim.Shared.Models;
using System.Threading.Tasks;

namespace AirportSim.Server.Hubs
{
    public interface ISimulationClient
    {
        Task ReceiveSnapshot(SimSnapshot snapshot);   
        Task ReceiveAlert(string message);            
    }

    public class SimulationHub : Hub<ISimulationClient>
    {
        public async Task SetTimeScale(double scale)
        {
            await Clients.All.ReceiveAlert($"Time scale adjusted to {scale}x");
        }

        public async Task SetPaused(bool isPaused)
        {
            string status = isPaused ? "Paused" : "Resumed";
            await Clients.All.ReceiveAlert($"Simulation {status}");
        }
    }
}