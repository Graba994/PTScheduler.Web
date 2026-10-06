using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorePaymentGateways : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderGroupId",
                table: "ServiceOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentGateway",
                table: "ServiceOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentExternalId",
                table: "ServiceOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "ServiceOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalPaymentId",
                table: "PaymentRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceOrderId",
                table: "PaymentRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_OrderGroupId",
                table: "ServiceOrders",
                column: "OrderGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_PaymentExternalId",
                table: "ServiceOrders",
                column: "PaymentExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_ExternalPaymentId",
                table: "PaymentRecords",
                column: "ExternalPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_ServiceOrderId",
                table: "PaymentRecords",
                column: "ServiceOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentRecords_ServiceOrders_ServiceOrderId",
                table: "PaymentRecords",
                column: "ServiceOrderId",
                principalTable: "ServiceOrders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentRecords_ServiceOrders_ServiceOrderId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_ServiceOrderId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_PaymentRecords_ExternalPaymentId",
                table: "PaymentRecords");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrders_PaymentExternalId",
                table: "ServiceOrders");

            migrationBuilder.DropIndex(
                name: "IX_ServiceOrders_OrderGroupId",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "ServiceOrderId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "ExternalPaymentId",
                table: "PaymentRecords");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "PaymentExternalId",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "PaymentGateway",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "OrderGroupId",
                table: "ServiceOrders");
        }
    }
}
