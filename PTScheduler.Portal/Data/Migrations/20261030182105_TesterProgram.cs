using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TesterProgram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TesterSince",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TesterUntil",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Tester",
                table: "InviteCodes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TesterFeedback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Week = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Annoyed = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Missing = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TesterFeedback", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TesterFeedback_CreatedAt",
                table: "TesterFeedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TesterFeedback_TenantId_Week",
                table: "TesterFeedback",
                columns: new[] { "TenantId", "Week" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TesterFeedback");

            migrationBuilder.DropColumn(
                name: "TesterSince",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TesterUntil",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Tester",
                table: "InviteCodes");
        }
    }
}
