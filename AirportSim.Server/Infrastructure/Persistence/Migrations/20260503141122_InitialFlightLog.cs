using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AirportSim.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFlightLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flight_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    flight_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    aircraft_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    flight_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    origin = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    destination = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    assigned_gate = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    go_around_count = table.Column<int>(type: "integer", nullable: false),
                    delay_minutes = table.Column<int>(type: "integer", nullable: false),
                    final_fuel_pct = table.Column<double>(type: "double precision", precision: 5, scale: 2, nullable: false),
                    simulated_time = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    wall_clock_time = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flight_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_flight_log_flight_id",
                table: "flight_log",
                column: "flight_id");

            migrationBuilder.CreateIndex(
                name: "ix_flight_log_outcome",
                table: "flight_log",
                column: "outcome");

            migrationBuilder.CreateIndex(
                name: "ix_flight_log_simulated_time",
                table: "flight_log",
                column: "simulated_time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flight_log");
        }
    }
}
