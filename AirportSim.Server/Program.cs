using AirportSim.Server.Application.Commands;
using AirportSim.Server.Domain.Interfaces;
using AirportSim.Server.Infrastructure.Hubs;
using AirportSim.Server.Infrastructure.Persistence;
using AirportSim.Server.Infrastructure.Services;
using AirportSim.Server.Infrastructure.Simulation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── MediatR ───────────────────────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<GrantClearanceCommand>());

// ── PostgreSQL + EF Core ──────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<SimulationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres"),
        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "public")));

// ── Repositories ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IFlightLogRepository, FlightLogRepository>();

// ── Redis ─────────────────────────────────────────────────────────────────────
// AddStackExchangeRedisCache wires IDistributedCache → Redis.
// RedisCacheService wraps it behind ICacheService with graceful degradation.
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration         = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName          = "airportsim:";
});
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

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

// Auto-apply EF migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
                  .GetRequiredService<IDbContextFactory<SimulationDbContext>>()
                  .CreateDbContext();
    db.Database.Migrate();
}

app.UseCors();
app.MapHub<SimulationHub>("/simhub");
app.MapGet("/health", () => Results.Ok(new { status = "running", time = DateTimeOffset.UtcNow }));

app.MapGet("/api/flightlog", async (
    IFlightLogRepository repo,
    int count = 50,
    CancellationToken ct = default) =>
{
    var entries = await repo.GetRecentAsync(count, ct);
    return Results.Ok(entries);
});

app.Run();