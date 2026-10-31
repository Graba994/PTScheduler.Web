using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class MeetingsSurveyLegal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalAcceptances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: false),
                    DocKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalAcceptances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainerMeetings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TrainerName = table.Column<string>(type: "text", nullable: false),
                    Contact = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    NameWritten = table.Column<string>(type: "text", nullable: true),
                    NamePick = table.Column<string>(type: "text", nullable: true),
                    NameRecalled = table.Column<bool>(type: "boolean", nullable: true),
                    BookingToday = table.Column<string>(type: "text", nullable: true),
                    Clients = table.Column<int>(type: "integer", nullable: true),
                    NoShowsPerMonth = table.Column<int>(type: "integer", nullable: true),
                    Pains = table.Column<string>(type: "text", nullable: true),
                    DemoNotes = table.Column<string>(type: "text", nullable: true),
                    PriceTooCheap = table.Column<int>(type: "integer", nullable: true),
                    PriceBargain = table.Column<int>(type: "integer", nullable: true),
                    PriceExpensive = table.Column<int>(type: "integer", nullable: true),
                    PriceTooExpensive = table.Column<int>(type: "integer", nullable: true),
                    Committed = table.Column<bool>(type: "boolean", nullable: false),
                    InviteCode = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerMeetings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainerSurveyResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    WorkMode = table.Column<string>(type: "text", nullable: true),
                    Clients = table.Column<string>(type: "text", nullable: true),
                    Booking = table.Column<string>(type: "text", nullable: true),
                    NoShows = table.Column<string>(type: "text", nullable: true),
                    Pains = table.Column<string>(type: "text", nullable: true),
                    ToolsSpend = table.Column<string>(type: "text", nullable: true),
                    WouldPay = table.Column<string>(type: "text", nullable: true),
                    Wish = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    ContactConsent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainerSurveyResponses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_TenantId_DocKey_Version",
                table: "LegalAcceptances",
                columns: new[] { "TenantId", "DocKey", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalAcceptances");

            migrationBuilder.DropTable(
                name: "TrainerMeetings");

            migrationBuilder.DropTable(
                name: "TrainerSurveyResponses");
        }
    }
}
