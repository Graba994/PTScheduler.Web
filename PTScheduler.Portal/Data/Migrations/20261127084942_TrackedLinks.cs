using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackedLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignupSource",
                table: "Tenants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrackedLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PostId = table.Column<int>(type: "integer", nullable: true),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Target = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Clicks = table.Column<int>(type: "integer", nullable: false),
                    LastClickAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedLinks_InstagramPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "InstagramPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TrackedLinkDays",
                columns: table => new
                {
                    LinkId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Clicks = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedLinkDays", x => new { x.LinkId, x.Day });
                    table.ForeignKey(
                        name: "FK_TrackedLinkDays_TrackedLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "TrackedLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_SignupSource",
                table: "Tenants",
                column: "SignupSource");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedLinks_Code",
                table: "TrackedLinks",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedLinks_PostId_Channel",
                table: "TrackedLinks",
                columns: new[] { "PostId", "Channel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedLinkDays");

            migrationBuilder.DropTable(
                name: "TrackedLinks");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_SignupSource",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignupSource",
                table: "Tenants");
        }
    }
}
