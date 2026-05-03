using AirportSim.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AirportSim.Server.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext — the only class in the solution that imports
/// Microsoft.EntityFrameworkCore directly in the Infrastructure layer.
/// All schema configuration is done here via Fluent API (no data annotations
/// on the domain entity, keeping it framework-free).
/// </summary>
public class SimulationDbContext : DbContext
{
    public SimulationDbContext(DbContextOptions<SimulationDbContext> options)
        : base(options) { }

    public DbSet<FlightLogEntry> FlightLog => Set<FlightLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FlightLogEntry>(entity =>
        {
            entity.ToTable("flight_log");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                  .HasColumnName("id")
                  .UseIdentityByDefaultColumn();      // PostgreSQL IDENTITY

            entity.Property(e => e.FlightId)
                  .HasColumnName("flight_id")
                  .HasMaxLength(32)
                  .IsRequired();

            entity.Property(e => e.AircraftType)
                  .HasColumnName("aircraft_type")
                  .HasMaxLength(16)
                  .IsRequired();

            entity.Property(e => e.FlightType)
                  .HasColumnName("flight_type")
                  .HasMaxLength(16)
                  .IsRequired();

            entity.Property(e => e.Origin)
                  .HasColumnName("origin")
                  .HasMaxLength(8)
                  .IsRequired();

            entity.Property(e => e.Destination)
                  .HasColumnName("destination")
                  .HasMaxLength(8)
                  .IsRequired();

            entity.Property(e => e.AssignedGate)
                  .HasColumnName("assigned_gate")
                  .HasMaxLength(8)
                  .IsRequired();

            entity.Property(e => e.Outcome)
                  .HasColumnName("outcome")
                  .HasMaxLength(16)
                  .IsRequired();

            entity.Property(e => e.GoAroundCount)
                  .HasColumnName("go_around_count");

            entity.Property(e => e.DelayMinutes)
                  .HasColumnName("delay_minutes");

            entity.Property(e => e.FinalFuelPct)
                  .HasColumnName("final_fuel_pct")
                  .HasPrecision(5, 2);

            entity.Property(e => e.SimulatedTime)
                  .HasColumnName("simulated_time")
                  .HasColumnType("timestamptz");

            entity.Property(e => e.WallClockTime)
                  .HasColumnName("wall_clock_time")
                  .HasColumnType("timestamptz");

            // Index for the most common query patterns
            entity.HasIndex(e => e.SimulatedTime)
                  .HasDatabaseName("ix_flight_log_simulated_time");

            entity.HasIndex(e => e.FlightId)
                  .HasDatabaseName("ix_flight_log_flight_id");

            entity.HasIndex(e => e.Outcome)
                  .HasDatabaseName("ix_flight_log_outcome");
        });
    }
}