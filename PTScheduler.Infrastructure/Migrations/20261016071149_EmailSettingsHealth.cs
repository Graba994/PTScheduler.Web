using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmailSettingsHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FallbackCount",
                table: "EmailSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "EmailSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastErrorAt",
                table: "EmailSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOkAt",
                table: "EmailSettings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FallbackCount",
                table: "EmailSettings");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "EmailSettings");

            migrationBuilder.DropColumn(
                name: "LastErrorAt",
                table: "EmailSettings");

            migrationBuilder.DropColumn(
                name: "LastOkAt",
                table: "EmailSettings");
        }
    }
}
