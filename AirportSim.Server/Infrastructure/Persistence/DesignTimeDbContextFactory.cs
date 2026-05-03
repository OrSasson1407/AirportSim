using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AirportSim.Server.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by the EF Core CLI tools (dotnet ef migrations add, dotnet ef database update).
/// Not used at runtime — Program.cs handles the real DbContext registration.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SimulationDbContext>
{
    public SimulationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SimulationDbContext>();

        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=airportsim;Username=airportsim;Password=airportsim_dev");

        return new SimulationDbContext(optionsBuilder.Options);  // ← .Options not .Build()
    }
}