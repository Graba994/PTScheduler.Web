using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HantlyProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MutedHints",
                table: "TrainerConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtraReminderSentAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExtraReminder",
                table: "Clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ShortLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Clicks = table.Column<int>(type: "integer", nullable: false),
                    LastClickAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SlotInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    SlotStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SlotEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ShortLinkId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotInvitations_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SlotInvitations_ShortLinks_ShortLinkId",
                        column: x => x.ShortLinkId,
                        principalTable: "ShortLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortLinks_Code",
                table: "ShortLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlotInvitations_ClientId_SentAt",
                table: "SlotInvitations",
                columns: new[] { "ClientId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SlotInvitations_ShortLinkId",
                table: "SlotInvitations",
                column: "ShortLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_SlotInvitations_TrainerUserId_SlotStart",
                table: "SlotInvitations",
                columns: new[] { "TrainerUserId", "SlotStart" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlotInvitations");

            migrationBuilder.DropTable(
                name: "ShortLinks");

            migrationBuilder.DropColumn(
                name: "MutedHints",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "ExtraReminderSentAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ExtraReminder",
                table: "Clients");
        }
    }
}
