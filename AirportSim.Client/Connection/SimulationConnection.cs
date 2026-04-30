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

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<SimSnapshot>? OnSnapshotReceived;
        public event Action<string>?      OnAlertReceived;
        public event Action<List<string>>? OnAudioTriggersReceived; // NEW
        public event Action?              OnConnected;
        public event Action?              OnDisconnected;  // NEW

        // NEW: expose connection state for the UI status bar
        public HubConnectionState State => _hubConnection.State;
        public bool IsConnected => _hubConnection.State == HubConnectionState.Connected;

        public SimulationConnection()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl("http://localhost:2001/simhub")
                .WithAutomaticReconnect(new[]
                {
                    // NEW: graduated back-off — 0s, 2s, 5s, 10s, 30s, 30s, ...
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            // ── Incoming messages ─────────────────────────────────────────────
            _hubConnection.On<SimSnapshot>("ReceiveSnapshot", snap =>
                OnSnapshotReceived?.Invoke(snap));

            _hubConnection.On<string>("ReceiveAlert", msg =>
                OnAlertReceived?.Invoke(msg));

            _hubConnection.On<List<string>>("ReceiveAudioTriggers", files =>
                OnAudioTriggersReceived?.Invoke(files)); // NEW

            // ── Connection lifecycle ──────────────────────────────────────────
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

        // ── Commands → server ─────────────────────────────────────────────────

        public Task SetTimeScaleAsync(double scale)   => SendAsync("SetTimeScale", scale);
        public Task SetPausedAsync(bool isPaused)     => SendAsync("SetPaused", isPaused);
        public Task StepSpeedUpAsync()                => SendAsync("StepSpeedUp");
        public Task StepSpeedDownAsync()              => SendAsync("StepSpeedDown");
        public Task DeclareEmergencyAsync()           => SendAsync("DeclareEmergency");  // NEW
        public Task CycleWeatherAsync()               => SendAsync("CycleWeather");      // NEW

        // NEW: safe fire-and-forget — silently drops if not connected
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