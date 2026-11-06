using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class Garmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GarminAuthRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeVerifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    TenantClientId = table.Column<int>(type: "integer", nullable: false),
                    ReturnPath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GarminAuthRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GarminConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    TenantClientId = table.Column<int>(type: "integer", nullable: false),
                    GarminUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccessTokenProtected = table.Column<string>(type: "text", nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenProtected = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisconnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastDataAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GarminConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GarminConnections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GarminInbox",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Kinds = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GarminInbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GarminAuthRequests_State",
                table: "GarminAuthRequests",
                column: "State",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GarminConnections_GarminUserId",
                table: "GarminConnections",
                column: "GarminUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GarminConnections_TenantId_TenantClientId",
                table: "GarminConnections",
                columns: new[] { "TenantId", "TenantClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_GarminInbox_ProcessedAt_Id",
                table: "GarminInbox",
                columns: new[] { "ProcessedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GarminAuthRequests");

            migrationBuilder.DropTable(
                name: "GarminConnections");

            migrationBuilder.DropTable(
                name: "GarminInbox");
        }
    }
}
