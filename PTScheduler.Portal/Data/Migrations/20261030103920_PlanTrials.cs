using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlanTrials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TrialPlanEndsAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpgradeTrialUsedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            // Okresy próbne planu z poprzedniej wersji trzymały koniec w TrialEndsAt (okres próbny rozliczeń) — przenosimy.
            migrationBuilder.Sql("""
                UPDATE "Tenants" SET "TrialPlanEndsAt" = "TrialEndsAt", "TrialEndsAt" = NULL
                WHERE "TrialPlanId" IS NOT NULL AND "TrialPlanEndsAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrialPlanEndsAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "UpgradeTrialUsedAt",
                table: "Tenants");
        }
    }
}
