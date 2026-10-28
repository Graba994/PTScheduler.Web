using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WearableInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifySessions",
                table: "WearableLinks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyWeekly",
                table: "WearableLinks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WeeklyReportSentFor",
                table: "WearableLinks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SummarySentAt",
                table: "WearableActivities",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifySessions",
                table: "WearableLinks");

            migrationBuilder.DropColumn(
                name: "NotifyWeekly",
                table: "WearableLinks");

            migrationBuilder.DropColumn(
                name: "WeeklyReportSentFor",
                table: "WearableLinks");

            migrationBuilder.DropColumn(
                name: "SummarySentAt",
                table: "WearableActivities");
        }
    }
}
