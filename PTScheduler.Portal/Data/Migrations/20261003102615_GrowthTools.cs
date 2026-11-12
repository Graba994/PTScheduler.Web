using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class GrowthTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstPaidAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FreeMonths",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastFreeMonthPeriod",
                table: "Tenants",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetricsAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetricsClients",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetricsSessions",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OnboardingStage",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReferralRewardedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferredByTenantId",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FunnelCounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunnelCounters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ReferredByTenantId",
                table: "Tenants",
                column: "ReferredByTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FunnelCounters_Day_Kind",
                table: "FunnelCounters",
                columns: new[] { "Day", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FunnelCounters");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ReferredByTenantId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "FirstPaidAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "FreeMonths",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastFreeMonthPeriod",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MetricsAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MetricsClients",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MetricsSessions",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "OnboardingStage",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ReferralRewardedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ReferredByTenantId",
                table: "Tenants");
        }
    }
}
