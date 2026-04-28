using AirportSim.Server.Hubs;
using AirportSim.Server.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddSignalR();
builder.Services.AddHostedService<SimulationEngine>();

// Configure CORS for local development testing
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true) 
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();

// Map the SignalR Hub
app.MapHub<SimulationHub>("/simhub");

app.Run();