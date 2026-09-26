using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DownAlertSent",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHealthy",
                table: "Tenants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHealthCheckAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthError",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHealthResponseMs",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnhealthySinceUtc",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownAlertSent",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsHealthy",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastHealthError",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastHealthResponseMs",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "UnhealthySinceUtc",
                table: "Tenants");
        }
    }
}
