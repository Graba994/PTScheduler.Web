using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class BugReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BugReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PageTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AuthorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AuthorRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    Viewport = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ClientErrors = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Screenshot = table.Column<byte[]>(type: "bytea", nullable: true),
                    ScreenshotType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AdminNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BugReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BugReports_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_Status_CreatedAt",
                table: "BugReports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BugReports_TenantId",
                table: "BugReports",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BugReports");
        }
    }
}
