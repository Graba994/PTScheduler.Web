using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxThreads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SubjectKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Counterpart = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CounterpartName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<int>(type: "integer", nullable: true),
                    Folder = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Unread = table.Column<int>(type: "integer", nullable: false),
                    LastAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Snippet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxThreads_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ThreadId = table.Column<int>(type: "integer", nullable: false),
                    Incoming = table.Column<bool>(type: "boolean", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InReplyTo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    FromName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: true),
                    Html = table.Column<string>(type: "text", nullable: true),
                    SpamScore = table.Column<double>(type: "double precision", nullable: true),
                    AttachmentsJson = table.Column<string>(type: "text", nullable: true),
                    SentBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxMessages_InboxThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "InboxThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_MessageId",
                table: "InboxMessages",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ThreadId",
                table: "InboxMessages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxThreads_Counterpart_SubjectKey",
                table: "InboxThreads",
                columns: new[] { "Counterpart", "SubjectKey" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxThreads_Folder_LastAt",
                table: "InboxThreads",
                columns: new[] { "Folder", "LastAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxThreads_TenantId",
                table: "InboxThreads",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages");

            migrationBuilder.DropTable(
                name: "InboxThreads");
        }
    }
}
