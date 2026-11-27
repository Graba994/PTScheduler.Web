using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKsef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerAddress",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerCity",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerNip",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerPostalCode",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KsefError",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KsefInvoiceReference",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KsefNumber",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KsefSentAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KsefSessionReference",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KsefStatus",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "KsefApiUrl",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KsefEnabled",
                table: "FinanceTaxConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "KsefEnvironment",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KsefTokenProtected",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VatExemptBasis",
                table: "FinanceTaxConfigs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerCity",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerNip",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerPostalCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KsefError",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KsefInvoiceReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KsefNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KsefSentAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KsefSessionReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KsefStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "KsefApiUrl",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "KsefEnabled",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "KsefEnvironment",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "KsefTokenProtected",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "SellerName",
                table: "FinanceTaxConfigs");

            migrationBuilder.DropColumn(
                name: "VatExemptBasis",
                table: "FinanceTaxConfigs");
        }
    }
}
