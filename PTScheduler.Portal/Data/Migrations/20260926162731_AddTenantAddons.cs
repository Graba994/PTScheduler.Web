using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAddons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripePriceId",
                table: "ServiceItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantAddons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    ServiceItemId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StripeSubscriptionItemId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAddons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantAddons_ServiceItems_ServiceItemId",
                        column: x => x.ServiceItemId,
                        principalTable: "ServiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantAddons_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 1,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 2,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 3,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 4,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 5,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 6,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 7,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 8,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 100,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 101,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 102,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 110,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 111,
                column: "StripePriceId",
                value: null);

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 120,
                columns: new[] { "Description", "Name", "PriceType", "StripePriceId", "Unit" },
                values: new object[] { "Podnosi miesięczny limit odtwarzania kursów wideo o 100 GB. Doliczane do abonamentu — rezygnujesz, kiedy chcesz.", "Transfer wideo +100 GB miesięcznie", "monthly", null, "miesiąc" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantAddons_ServiceItemId",
                table: "TenantAddons",
                column: "ServiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAddons_TenantId_Status",
                table: "TenantAddons",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantAddons");

            migrationBuilder.DropColumn(
                name: "StripePriceId",
                table: "ServiceItems");

            migrationBuilder.UpdateData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 120,
                columns: new[] { "Description", "Name", "PriceType", "Unit" },
                values: new object[] { "Dodatkowy miesięczny transfer dla odtwarzania kursów wideo.", "Dodatkowe 100 GB transferu wideo", "one_time", "100 GB" });
        }
    }
}
