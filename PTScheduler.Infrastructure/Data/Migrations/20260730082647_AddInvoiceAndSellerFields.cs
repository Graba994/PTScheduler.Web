using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceAndSellerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceIssuedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerAddress",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCity",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerNip",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPostalCode",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceIssuedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SellerAddress",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "SellerCity",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "SellerNip",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "SellerPostalCode",
                table: "FinanceTaxConfigs");
        }
    }
}
