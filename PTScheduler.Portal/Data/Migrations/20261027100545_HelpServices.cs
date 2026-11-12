using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class HelpServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ServiceItems",
                columns: new[] { "Id", "Category", "CreatedAt", "CreditAmount", "DefaultPrice", "Description", "FulfillmentType", "Icon", "IsActive", "Name", "PriceType", "SortOrder", "StripePriceId", "Unit" },
                values: new object[,]
                {
                    { 9, "setup", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), 0, 49m, "Podłączymy Twoją pocztę (Gmail, WP, Onet, firmową), żeby e-maile do klientów wychodziły z Twojego adresu. Wyślemy test i sprawdzimy, czy nie trafiają do spamu.", "manual", "bi-envelope-at", true, "Podłączenie własnej skrzynki e-mail", "one_time", 7, null, null },
                    { 10, "setup", new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), 0, 99m, "Ułożymy Twoją stronę: teksty o Tobie i ofercie, zdjęcia, opinie i kontakt — gotowa do pokazania klientom.", "manual", "bi-window-desktop", true, "Strona główna pod klucz", "one_time", 8, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "ServiceItems",
                keyColumn: "Id",
                keyValue: 10);
        }
    }
}
