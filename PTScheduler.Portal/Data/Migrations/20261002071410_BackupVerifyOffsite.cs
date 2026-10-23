using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackupVerifyOffsite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OffsiteAt",
                table: "BackupEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OffsiteInfo",
                table: "BackupEntries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OffsiteOk",
                table: "BackupEntries",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "BackupEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifyInfo",
                table: "BackupEntries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VerifyOk",
                table: "BackupEntries",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OffsiteAt",
                table: "BackupEntries");

            migrationBuilder.DropColumn(
                name: "OffsiteInfo",
                table: "BackupEntries");

            migrationBuilder.DropColumn(
                name: "OffsiteOk",
                table: "BackupEntries");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "BackupEntries");

            migrationBuilder.DropColumn(
                name: "VerifyInfo",
                table: "BackupEntries");

            migrationBuilder.DropColumn(
                name: "VerifyOk",
                table: "BackupEntries");
        }
    }
}
