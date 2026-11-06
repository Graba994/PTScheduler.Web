using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageReminderFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryNotifiedAt",
                table: "SessionPackages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LowCreditsNotifiedAt",
                table: "SessionPackages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiryNotifiedAt",
                table: "SessionPackages");

            migrationBuilder.DropColumn(
                name: "LowCreditsNotifiedAt",
                table: "SessionPackages");
        }
    }
}
