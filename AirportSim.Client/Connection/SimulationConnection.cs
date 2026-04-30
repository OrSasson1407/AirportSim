using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using AirportSim.Shared.Models;

namespace AirportSim.Client.Connection
{
    public class SimulationConnection
    {
        private readonly HubConnection _hubConnection;

        public event Action<SimSnapshot>? OnSnapshotReceived;
        public event Action<string>?      OnAlertReceived;
        public event Action<List<string>>? OnAudioTriggersReceived; 
        public event Action?              OnConnected;
        public event Action?              OnDisconnected;  

        public HubConnectionState State => _hubConnection.State;
        public bool IsConnected => _hubConnection.State == HubConnectionState.Connected;

        public SimulationConnection()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("http://localhost:2001/simhub")
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            _hubConnection.On<SimSnapshot>("ReceiveSnapshot", snap =>
                OnSnapshotReceived?.Invoke(snap));

            _hubConnection.On<string>("ReceiveAlert", msg =>
                OnAlertReceived?.Invoke(msg));

            _hubConnection.On<List<string>>("ReceiveAudioTriggers", files =>
                OnAudioTriggersReceived?.Invoke(files)); 

            _hubConnection.Reconnected += _ =>
            {
                OnConnected?.Invoke();
                return Task.CompletedTask;
            };

            _hubConnection.Reconnecting += _ =>
            {
                OnDisconnected?.Invoke();
                return Task.CompletedTask;
            };

            _hubConnection.Closed += _ =>
            {
                OnDisconnected?.Invoke();
                return Task.CompletedTask;
            };
        }

        public async Task ConnectAsync()
        {
            try
            {
                await _hubConnection.StartAsync();
                OnConnected?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimulationConnection] Connect failed: {ex.Message}");
                OnDisconnected?.Invoke();
            }
        }

        public Task SetTimeScaleAsync(double scale)        => SendAsync("SetTimeScale", scale);
        public Task SetPausedAsync(bool isPaused)          => SendAsync("SetPaused", isPaused);
        public Task StepSpeedUpAsync()                     => SendAsync("StepSpeedUp");
        public Task StepSpeedDownAsync()                   => SendAsync("StepSpeedDown");
        public Task DeclareEmergencyAsync()                => SendAsync("DeclareEmergency");  
        public Task CycleWeatherAsync()                    => SendAsync("CycleWeather");      
        public Task SetAirportLayoutAsync(string layoutId) => SendAsync("SetAirportLayout", layoutId);
        public Task SetRvrAsync(int rvrMeters)             => SendAsync("SetRvr", rvrMeters); // NEW

        private async Task SendAsync(string method, params object[] args)
        {
            if (!IsConnected) return;
            try
            {
                await _hubConnection.SendCoreAsync(method, args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimulationConnection] Send '{method}' failed: {ex.Message}");
            }
        }
    }
}