using AirportSim.Server.Application.Commands;
using AirportSim.Server.Application.Queries;
using AirportSim.Server.Infrastructure.Simulation;
using AirportSim.Shared.Models;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace AirportSim.Server.Infrastructure.Hubs;

public interface ISimulationClient
{
    Task ReceiveSnapshot(SimSnapshot snapshot);
    Task ReceiveAlert(string message);
    Task ReceiveAudioTriggers(List<string> audioFiles);
}

/// <summary>
/// Presentation layer — the only job of this class is to translate
/// SignalR method calls into MediatR messages and broadcast the result.
/// It has NO knowledge of simulation logic, services, or infrastructure.
/// </summary>
public class SimulationHub : Hub<ISimulationClient>
{
    private readonly IMediator _mediator;

    public SimulationHub(IMediator mediator) => _mediator = mediator;

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task SetTimeScale(double scale)
    {
        var alert = await _mediator.Send(new SetTimeScaleCommand(scale));
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task SetPaused(bool isPaused)
    {
        var alert = await _mediator.Send(new SetPausedCommand(isPaused));
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task StepSpeedUp()
    {
        var alert = await _mediator.Send(new StepSpeedUpCommand());
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task StepSpeedDown()
    {
        var alert = await _mediator.Send(new StepSpeedDownCommand());
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task DeclareEmergency()
    {
        var alert = await _mediator.Send(new DeclareEmergencyCommand());
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task CycleWeather()
    {
        var alert = await _mediator.Send(new CycleWeatherCommand());
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task SetAirportLayout(string layoutId)
    {
        var alert = await _mediator.Send(new LoadLayoutCommand(layoutId));
        await Clients.All.ReceiveAlert(alert);
    }

    public Task SetRvr(int rvrMeters)
        => _mediator.Send(new SetRvrCommand(rvrMeters));

    public async Task GrantClearance(string flightId, string clearanceType)
    {
        var alert = await _mediator.Send(new GrantClearanceCommand(flightId, clearanceType));
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task AssignSpeed(string flightId, int speedKts)
    {
        var alert = await _mediator.Send(new AssignSpeedCommand(flightId, speedKts));
        await Clients.All.ReceiveAlert(alert);
    }

    public async Task AssignAltitude(string flightId, int altitudeFt)
    {
        var alert = await _mediator.Send(new AssignAltitudeCommand(flightId, altitudeFt));
        await Clients.All.ReceiveAlert(alert);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public Task<List<SimSnapshot>> RequestReplayBuffer()
        => _mediator.Send(new GetReplayBufferQuery());

    public Task<SimSnapshot?> RequestLatestSnapshot()
        => _mediator.Send(new GetLatestSnapshotQuery());
}