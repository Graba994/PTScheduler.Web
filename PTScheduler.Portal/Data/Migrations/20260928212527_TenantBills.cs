using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantBills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidVia = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PayToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaymentGateway = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaymentExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EmailSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReminderCount = table.Column<int>(type: "integer", nullable: false),
                    LastReminderAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantBills_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantBillLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    OfferItemId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBillLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantBillLines_TenantBills_BillId",
                        column: x => x.BillId,
                        principalTable: "TenantBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBillLines_BillId",
                table: "TenantBillLines",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBills_Number",
                table: "TenantBills",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantBills_PaymentExternalId",
                table: "TenantBills",
                column: "PaymentExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBills_PaymentSessionId",
                table: "TenantBills",
                column: "PaymentSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantBills_PayToken",
                table: "TenantBills",
                column: "PayToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantBills_TenantId_PeriodStart",
                table: "TenantBills",
                columns: new[] { "TenantId", "PeriodStart" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantBillLines");

            migrationBuilder.DropTable(
                name: "TenantBills");
        }
    }
}
