using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Connection
{
    public class SimulationConnection
    {
        private readonly HubConnection _hubConnection;
        
        public event Action<SimSnapshot>? OnSnapshotReceived;
        public event Action<string>? OnAlertReceived;

        public SimulationConnection()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("http://localhost:2001/simhub")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<SimSnapshot>("ReceiveSnapshot", snapshot =>
            {
                OnSnapshotReceived?.Invoke(snapshot);
            });

            _hubConnection.On<string>("ReceiveAlert", message =>
            {
                OnAlertReceived?.Invoke(message);
            });
        }

        public async Task ConnectAsync()
        {
            try
            {
                await _hubConnection.StartAsync();
                Console.WriteLine("Connected to Simulation Server.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
            }
        }

        public async Task SetTimeScaleAsync(double scale)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.SendAsync("SetTimeScale", scale);
            }
        }

        public async Task SetPausedAsync(bool isPaused)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.SendAsync("SetPaused", isPaused);
            }
        }
    }
}