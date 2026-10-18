using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WeekTemplateSlotWatchRebuyAccountant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FreedNotifiedAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RebuyNotifiedAt",
                table: "SessionPackages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AccountantAutoSend",
                table: "FinanceTaxConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AccountantEmail",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountantLastSentMonth",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SlotWatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    FromTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ToTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotWatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotWatches_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlotWatches_ClientId",
                table: "SlotWatches",
                column: "ClientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotWatches");

            migrationBuilder.DropColumn(
                name: "FreedNotifiedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "RebuyNotifiedAt",
                table: "SessionPackages");

            migrationBuilder.DropColumn(
                name: "AccountantAutoSend",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "AccountantEmail",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "AccountantLastSentMonth",
                table: "FinanceTaxConfigs");
        }
    }
}
