using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    Client1Id = table.Column<int>(type: "integer", nullable: false),
                    Client2Id = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientContacts_Clients_Client1Id",
                        column: x => x.Client1Id,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientContacts_Clients_Client2Id",
                        column: x => x.Client2Id,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    InvitedClientId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResponseNote = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionInvitations_Clients_InvitedClientId",
                        column: x => x.InvitedClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionInvitations_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    SessionTypeId = table.Column<int>(type: "integer", nullable: false),
                    RecurrenceDays = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionSeries_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionSeries_SessionTypes_SessionTypeId",
                        column: x => x.SessionTypeId,
                        principalTable: "SessionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainerAvailabilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: true),
                    SpecificDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Label = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerAvailabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainerConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TrainerUserId = table.Column<string>(type: "text", nullable: false),
                    BreakAfterSessionMinutes = table.Column<int>(type: "integer", nullable: false),
                    SlotGranularityMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowClientsDiscoverPeers = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_SeriesId",
                table: "Sessions",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_Client1Id_Client2Id",
                table: "ClientContacts",
                columns: new[] { "Client1Id", "Client2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_Client2Id",
                table: "ClientContacts",
                column: "Client2Id");

            migrationBuilder.CreateIndex(
                name: "IX_SessionInvitations_InvitedClientId",
                table: "SessionInvitations",
                column: "InvitedClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionInvitations_SessionId",
                table: "SessionInvitations",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionSeries_ClientId",
                table: "SessionSeries",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionSeries_SessionTypeId",
                table: "SessionSeries",
                column: "SessionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainerConfigs_TrainerUserId",
                table: "TrainerConfigs",
                column: "TrainerUserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_SessionSeries_SeriesId",
                table: "Sessions",
                column: "SeriesId",
                principalTable: "SessionSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_SessionSeries_SeriesId",
                table: "Sessions");

            migrationBuilder.DropTable(
                name: "ClientContacts");

            migrationBuilder.DropTable(
                name: "SessionInvitations");

            migrationBuilder.DropTable(
                name: "SessionSeries");

            migrationBuilder.DropTable(
                name: "TrainerAvailabilities");

            migrationBuilder.DropTable(
                name: "TrainerConfigs");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_SeriesId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Sessions");
        }
    }
}
