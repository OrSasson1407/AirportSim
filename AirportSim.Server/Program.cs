using AirportSim.Server.Hubs;
using AirportSim.Server.Services;
using AirportSim.Server.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddSignalR(options =>
{
    // NEW: increase max message size for large snapshots with many aircraft
    options.MaximumReceiveMessageSize = 512 * 1024; // 512 KB
});

// NEW: SimulationEngine registered as singleton so SimulationHub can inject it
builder.Services.AddSingleton<SimulationEngine>();
builder.Services.AddHostedService(p => p.GetRequiredService<SimulationEngine>());

// Diagnostic heartbeat service
builder.Services.AddHostedService<BroadcastService>();

// CORS for local dev — allow any origin with credentials (SignalR requirement)
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

// NEW: minimal health endpoint — useful for load balancer checks
app.MapGet("/health", () => Results.Ok(new { status = "running", time = DateTimeOffset.UtcNow }));

app.Run();