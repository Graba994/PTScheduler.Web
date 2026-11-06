using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCentralizedReselling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TenantCredits table
            migrationBuilder.CreateTable(
                name: "TenantCredits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    CreditType = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalPurchased = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalUsed = table.Column<decimal>(type: "numeric", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantCredits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantCredits_TenantId_CreditType",
                table: "TenantCredits",
                columns: new[] { "TenantId", "CreditType" },
                unique: true);

            // ServiceItem new columns
            migrationBuilder.AddColumn<string>(
                name: "FulfillmentType",
                table: "ServiceItems",
                type: "text",
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<int>(
                name: "CreditAmount",
                table: "ServiceItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceItems_FulfillmentType",
                table: "ServiceItems",
                column: "FulfillmentType");

            // Seed SMS credit packs
            migrationBuilder.InsertData(
                table: "ServiceItems",
                columns: new[] { "Id", "Name", "Description", "Category", "DefaultPrice", "PriceType", "Unit", "Icon", "FulfillmentType", "CreditAmount", "SortOrder", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 100, "Pakiet 50 SMS", "50 wiadomości SMS do wysyłania przypomnień klientom.", "addon", 15m, "one_time", "50 szt.", "bi-chat-dots", "credit_sms", 50, 20, true, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 101, "Pakiet 200 SMS", "200 wiadomości SMS — najlepsza wartość dla aktywnych trenerów.", "addon", 50m, "one_time", "200 szt.", "bi-chat-dots", "credit_sms", 200, 21, true, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 102, "Pakiet 500 SMS", "500 wiadomości SMS — dla dużych studiów treningowych.", "addon", 100m, "one_time", "500 szt.", "bi-chat-dots", "credit_sms", 500, 22, true, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) }
                });

            // Seed CDN packs
            migrationBuilder.InsertData(
                table: "ServiceItems",
                columns: new[] { "Id", "Name", "Description", "Category", "DefaultPrice", "PriceType", "Unit", "Icon", "FulfillmentType", "CreditAmount", "SortOrder", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 110, "Dodatkowe 10 GB wideo", "Rozszerzenie przestrzeni na kursy wideo o 10 GB.", "addon", 20m, "one_time", "10 GB", "bi-cloud-upload", "credit_cdn_storage", 10, 30, true, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 111, "Dodatkowe 50 GB wideo", "Rozszerzenie przestrzeni na kursy wideo o 50 GB.", "addon", 80m, "one_time", "50 GB", "bi-cloud-upload", "credit_cdn_storage", 50, 31, true, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 120, "Dodatkowe 100 GB transferu wideo", "Dodatkowy miesięczny transfer dla odtwarzania kursów wideo.", "addon", 25m, "one_time", "100 GB", "bi-speedometer2", "credit_cdn_bandwidth", 100, 32, true, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove seeded service items
            migrationBuilder.DeleteData(table: "ServiceItems", keyColumn: "Id", keyValue: 100);
            migrationBuilder.DeleteData(table: "ServiceItems", keyColumn: "Id", keyValue: 101);
            migrationBuilder.DeleteData(table: "ServiceItems", keyColumn: "Id", keyValue: 102);
            migrationBuilder.DeleteData(table: "ServiceItems", keyColumn: "Id", keyValue: 110);
            migrationBuilder.DeleteData(table: "ServiceItems", keyColumn: "Id", keyValue: 111);
            migrationBuilder.DeleteData(table: "ServiceItems", keyColumn: "Id", keyValue: 120);

            migrationBuilder.DropIndex(
                name: "IX_ServiceItems_FulfillmentType",
                table: "ServiceItems");

            migrationBuilder.DropColumn(
                name: "CreditAmount",
                table: "ServiceItems");

            migrationBuilder.DropColumn(
                name: "FulfillmentType",
                table: "ServiceItems");

            migrationBuilder.DropTable(
                name: "TenantCredits");
        }
    }
}
