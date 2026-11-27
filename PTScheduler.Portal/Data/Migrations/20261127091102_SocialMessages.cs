using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class SocialMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PrivateReplyAt",
                table: "InstagramComments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuleId",
                table: "InstagramComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageId",
                table: "InstagramAccounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommentRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Keyword = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PublicReply = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Target = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LinkId = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Uses = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentRules_TrackedLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "TrackedLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SocialThreads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParticipantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParticipantName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Snippet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastInboundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Unread = table.Column<bool>(type: "boolean", nullable: false),
                    Done = table.Column<bool>(type: "boolean", nullable: false),
                    Suggestion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ThreadId = table.Column<int>(type: "integer", nullable: false),
                    Inbound = table.Column<bool>(type: "boolean", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    HasAttachment = table.Column<bool>(type: "boolean", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialMessages_SocialThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "SocialThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommentRules_LinkId",
                table: "CommentRules",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialMessages_ThreadId_At",
                table: "SocialMessages",
                columns: new[] { "ThreadId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialThreads_LastMessageAt",
                table: "SocialThreads",
                column: "LastMessageAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialThreads_Platform_ExternalId",
                table: "SocialThreads",
                columns: new[] { "Platform", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentRules");

            migrationBuilder.DropTable(
                name: "SocialMessages");

            migrationBuilder.DropTable(
                name: "SocialThreads");

            migrationBuilder.DropColumn(
                name: "PrivateReplyAt",
                table: "InstagramComments");

            migrationBuilder.DropColumn(
                name: "RuleId",
                table: "InstagramComments");

            migrationBuilder.DropColumn(
                name: "PageId",
                table: "InstagramAccounts");
        }
    }
}
