using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class SocialMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ToInstagram",
                table: "InstagramPosts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "InstagramMedia",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "instagram");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "InstagramComments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "instagram");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "InstagramAccounts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "instagram");

            migrationBuilder.CreateTable(
                name: "SocialChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    Api = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Default = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Handle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PictureUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProfileUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Token = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Followers = table.Column<int>(type: "integer", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialPostTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    ChannelId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Caption = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    ContainerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Permalink = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPostTargets_InstagramPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "InstagramPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SocialPostTargets_SocialChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "SocialChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostTargets_ChannelId",
                table: "SocialPostTargets",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostTargets_PostId_ChannelId",
                table: "SocialPostTargets",
                columns: new[] { "PostId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostTargets_Status",
                table: "SocialPostTargets",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPostTargets");

            migrationBuilder.DropTable(
                name: "SocialChannels");

            migrationBuilder.DropColumn(
                name: "ToInstagram",
                table: "InstagramPosts");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "InstagramMedia");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "InstagramComments");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "InstagramAccounts");
        }
    }
}
