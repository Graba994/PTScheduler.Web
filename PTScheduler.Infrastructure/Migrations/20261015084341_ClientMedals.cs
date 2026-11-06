using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClientMedals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PushMedals",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ClientMedals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientMedals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientMedals_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientMedals_ClientId_Key",
                table: "ClientMedals",
                columns: new[] { "ClientId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientMedals");

            migrationBuilder.DropColumn(
                name: "PushMedals",
                table: "NotificationPreferences");
        }
    }
}
