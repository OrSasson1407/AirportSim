using AirportSim.Shared.Models;

namespace AirportSim.Server.Domain.Interfaces;

/// <summary>
/// Abstraction for pushing simulation state to connected clients.
/// The Application layer calls this; the Infrastructure layer (SignalR) implements it.
/// This inversion keeps the simulation engine completely free of SignalR.
/// </summary>
public interface IBroadcastService
{
    Task BroadcastSnapshotAsync(SimSnapshot snapshot, CancellationToken ct = default);
    Task BroadcastAlertAsync(string message, CancellationToken ct = default);
    Task BroadcastAudioTriggersAsync(List<string> audioFiles, CancellationToken ct = default);
}