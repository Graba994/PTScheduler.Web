using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdPromotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Account = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CampaignId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AdSetId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreativeId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AdId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DailyBudget = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AgeMin = table.Column<int>(type: "integer", nullable: false),
                    AgeMax = table.Column<int>(type: "integer", nullable: false),
                    InterestsJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OnInstagram = table.Column<bool>(type: "boolean", nullable: false),
                    OnFacebook = table.Column<bool>(type: "boolean", nullable: false),
                    LinkId = table.Column<int>(type: "integer", nullable: true),
                    Spend = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Impressions = table.Column<int>(type: "integer", nullable: false),
                    Reach = table.Column<int>(type: "integer", nullable: false),
                    Clicks = table.Column<int>(type: "integer", nullable: false),
                    Engagements = table.Column<int>(type: "integer", nullable: false),
                    InsightsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdPromotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdPromotions_InstagramPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "InstagramPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AdPromotions_TrackedLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "TrackedLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdPromotions_LinkId",
                table: "AdPromotions",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_AdPromotions_PostId",
                table: "AdPromotions",
                column: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdPromotions");
        }
    }
}
