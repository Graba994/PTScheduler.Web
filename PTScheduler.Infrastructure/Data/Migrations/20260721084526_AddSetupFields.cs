using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetupFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SetupCompleted",
                table: "AppBrandings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SetupCompletedAt",
                table: "AppBrandings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SetupMode",
                table: "AppBrandings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupCompleted",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "SetupCompletedAt",
                table: "AppBrandings");

            migrationBuilder.DropColumn(
                name: "SetupMode",
                table: "AppBrandings");
        }
    }
}
