using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class Uptime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UptimeBeats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MonitorId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Up = table.Column<bool>(type: "boolean", nullable: false),
                    Ms = table.Column<int>(type: "integer", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptimeBeats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UptimeIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MonitorId = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptimeIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UptimeMonitors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Keyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    RetriesBeforeDown = table.Column<int>(type: "integer", nullable: false),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LastCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StateSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastResponseMs = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailuresInRow = table.Column<int>(type: "integer", nullable: false),
                    CertExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CertAlertSentOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptimeMonitors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UptimeBeats_At",
                table: "UptimeBeats",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_UptimeBeats_MonitorId_At",
                table: "UptimeBeats",
                columns: new[] { "MonitorId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_UptimeIncidents_MonitorId_StartedAt",
                table: "UptimeIncidents",
                columns: new[] { "MonitorId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UptimeMonitors_TenantId",
                table: "UptimeMonitors",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UptimeBeats");

            migrationBuilder.DropTable(
                name: "UptimeIncidents");

            migrationBuilder.DropTable(
                name: "UptimeMonitors");
        }
    }
}
