using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    public partial class AddServiceMarketplace : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    DefaultPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    PriceType = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantServicePrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    ServiceItemId = table.Column<int>(type: "integer", nullable: false),
                    CustomPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantServicePrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantServicePrices_ServiceItems_ServiceItemId",
                        column: x => x.ServiceItemId,
                        principalTable: "ServiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantServicePrices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    ServiceItemId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceOrders_ServiceItems_ServiceItemId",
                        column: x => x.ServiceItemId,
                        principalTable: "ServiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceOrders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceItems_Category",
                table: "ServiceItems",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceItems_IsActive",
                table: "ServiceItems",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TenantServicePrices_ServiceItemId",
                table: "TenantServicePrices",
                column: "ServiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantServicePrices_TenantId_ServiceItemId",
                table: "TenantServicePrices",
                columns: new[] { "TenantId", "ServiceItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_CreatedAt",
                table: "ServiceOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_Status",
                table: "ServiceOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_TenantId",
                table: "ServiceOrders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_ServiceItemId",
                table: "ServiceOrders",
                column: "ServiceItemId");

            // Seed service items
            migrationBuilder.InsertData(
                table: "ServiceItems",
                columns: new[] { "Id", "Name", "Description", "Category", "DefaultPrice", "PriceType", "Unit", "SortOrder", "IsActive", "Icon", "CreatedAt" },
                values: new object[,]
                {
                    { 1, "Zmiana logo / kolorów strony", "Wymiana logo, dopasowanie kolorystyki i motywu strony trenera.", "branding", 30m, "one_time", null, 1, true, "bi-palette", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) },
                    { 2, "Konfiguracja grafiku zajęć", "Ustawienie typów wizyt, godzin pracy, cyklicznych zajęć.", "setup", 30m, "one_time", null, 2, true, "bi-calendar-week", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) },
                    { 3, "Ustawienie płatności online", "Konfiguracja PayU lub Przelewy24, testowanie procesu płatności.", "setup", 50m, "one_time", null, 3, true, "bi-credit-card", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) },
                    { 4, "Import bazy klientów", "Import listy klientów z pliku Excel/CSV do systemu.", "setup", 50m, "one_time", null, 4, true, "bi-people", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) },
                    { 5, "Szkolenie 1:1 (30 min)", "Indywidualne szkolenie wideo z obsługi systemu.", "training", 80m, "one_time", null, 5, true, "bi-camera-video", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) },
                    { 6, "Pełna konfiguracja strony", "Kompleksowe ustawienie strony: branding, grafik, usługi, płatności.", "setup", 150m, "one_time", null, 6, true, "bi-wrench-adjustable", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) },
                    { 7, "Pakiet Wsparcie Podstawowy", "2 drobne zmiany/mies., email 24h, 1 szkolenie/kwartał.", "support", 29m, "monthly", "miesiąc", 10, true, "bi-headset", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) },
                    { 8, "Pakiet Wsparcie Premium", "Bez limitu drobnych zmian, priorytet + telefon, 1 szkolenie/mies.", "support", 79m, "monthly", "miesiąc", 11, true, "bi-star", new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc) }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ServiceOrders");
            migrationBuilder.DropTable(name: "TenantServicePrices");
            migrationBuilder.DropTable(name: "ServiceItems");
        }
    }
}
