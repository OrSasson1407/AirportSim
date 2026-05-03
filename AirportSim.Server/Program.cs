using AirportSim.Server.Application.Commands;   // anchor for assembly scan
using AirportSim.Server.Domain.Interfaces;
using AirportSim.Server.Infrastructure.Hubs;
using AirportSim.Server.Infrastructure.Services;
using AirportSim.Server.Infrastructure.Simulation;

var builder = WebApplication.CreateBuilder(args);

// ── MediatR — scans Application layer for all IRequestHandler<> registrations ─
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<GrantClearanceCommand>());

// ── SignalR ───────────────────────────────────────────────────────────────────
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 512 * 1024;
});

// ── Infrastructure: Broadcast ─────────────────────────────────────────────────
builder.Services.AddSingleton<IBroadcastService, SignalRBroadcastService>();

// ── Application: Simulation Engine ───────────────────────────────────────────
builder.Services.AddSingleton<SimulationEngine>();
builder.Services.AddSingleton<ISimulationService>(p => p.GetRequiredService<SimulationEngine>());
builder.Services.AddHostedService(p => p.GetRequiredService<SimulationEngine>());

// ── Infrastructure: Diagnostic heartbeat ─────────────────────────────────────
builder.Services.AddHostedService<BroadcastService>();

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true)
              .AllowCredentials());
});

// ── Pipeline ──────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseCors();
app.MapHub<SimulationHub>("/simhub");
app.MapGet("/health", () => Results.Ok(new { status = "running", time = DateTimeOffset.UtcNow }));

app.Run();