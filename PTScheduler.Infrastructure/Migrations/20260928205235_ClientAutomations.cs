using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClientAutomations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TrainerMessages",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AutomationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Channels = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ReturnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationLogs_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    EnabledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DelayDays = table.Column<int>(type: "integer", nullable: false),
                    CooldownDays = table.Column<int>(type: "integer", nullable: false),
                    ViaEmail = table.Column<bool>(type: "boolean", nullable: false),
                    ViaPush = table.Column<bool>(type: "boolean", nullable: false),
                    ViaSms = table.Column<bool>(type: "boolean", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ButtonText = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CouponPercent = table.Column<int>(type: "integer", nullable: false),
                    CouponValidDays = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationLogs_ClientId_Kind",
                table: "AutomationLogs",
                columns: new[] { "ClientId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationLogs_SentAt",
                table: "AutomationLogs",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_Kind",
                table: "AutomationRules",
                column: "Kind",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationLogs");

            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropColumn(
                name: "TrainerMessages",
                table: "NotificationPreferences");
        }
    }
}
