using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class StudioFilmy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudioJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VideoId = table.Column<int>(type: "integer", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    RunAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudioVideos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromotionId = table.Column<int>(type: "integer", nullable: true),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Brief = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Garmin = table.Column<bool>(type: "boolean", nullable: false),
                    Manual = table.Column<bool>(type: "boolean", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TagsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourcesJson = table.Column<string>(type: "jsonb", nullable: false),
                    HooksJson = table.Column<string>(type: "jsonb", nullable: false),
                    PinnedComment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ReviewComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    QaJson = table.Column<string>(type: "jsonb", nullable: true),
                    VideoSeconds = table.Column<double>(type: "double precision", nullable: true),
                    RenderedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioVideos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudioScenes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VideoId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    Headline = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SpeechText = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    StoryJson = table.Column<string>(type: "jsonb", nullable: false),
                    NeedsVoice = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioScenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudioScenes_StudioVideos_VideoId",
                        column: x => x.VideoId,
                        principalTable: "StudioVideos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudioJobs_Status_RunAfter",
                table: "StudioJobs",
                columns: new[] { "Status", "RunAfter" });

            migrationBuilder.CreateIndex(
                name: "IX_StudioJobs_VideoId",
                table: "StudioJobs",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_StudioScenes_VideoId_Order",
                table: "StudioScenes",
                columns: new[] { "VideoId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_StudioVideos_PromotionId",
                table: "StudioVideos",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudioVideos_Slug",
                table: "StudioVideos",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudioVideos_Status",
                table: "StudioVideos",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudioJobs");

            migrationBuilder.DropTable(
                name: "StudioScenes");

            migrationBuilder.DropTable(
                name: "StudioVideos");
        }
    }
}
