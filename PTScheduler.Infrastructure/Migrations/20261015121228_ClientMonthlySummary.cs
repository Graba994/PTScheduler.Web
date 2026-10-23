using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClientMonthlySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MonthlySummary",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MonthlySummarySentFor",
                table: "NotificationPreferences",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PushMonthlySummary",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonthlySummary",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "MonthlySummarySentFor",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "PushMonthlySummary",
                table: "NotificationPreferences");
        }
    }
}
