using AirportSim.Server.Domain.Interfaces;
using AirportSim.Server.Infrastructure.Hubs;
using AirportSim.Shared.Models;
using Microsoft.AspNetCore.SignalR;

namespace AirportSim.Server.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of IBroadcastService.
/// This is the ONLY file in the solution that is allowed to reference IHubContext.
/// </summary>
public sealed class SignalRBroadcastService : IBroadcastService
{
    private readonly IHubContext<SimulationHub, ISimulationClient> _hub;

    public SignalRBroadcastService(IHubContext<SimulationHub, ISimulationClient> hub)
    {
        _hub = hub;
    }

    public Task BroadcastSnapshotAsync(SimSnapshot snapshot, CancellationToken ct = default)
        => _hub.Clients.All.ReceiveSnapshot(snapshot);

    public Task BroadcastAlertAsync(string message, CancellationToken ct = default)
        => _hub.Clients.All.ReceiveAlert(message);

    public Task BroadcastAudioTriggersAsync(List<string> audioFiles, CancellationToken ct = default)
        => _hub.Clients.All.ReceiveAudioTriggers(audioFiles);
}