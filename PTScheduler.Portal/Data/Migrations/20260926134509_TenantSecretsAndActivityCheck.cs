using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantSecretsAndActivityCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InternalSecret",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityCheckedAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InternalSecret",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastActivityCheckedAt",
                table: "Tenants");
        }
    }
}
