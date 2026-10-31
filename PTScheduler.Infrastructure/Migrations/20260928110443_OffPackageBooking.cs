using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OffPackageBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Istniejący trenerzy dostają zalecane ustawienia: „kup pakiet”, „zapłać online” i „zapłacę u trenera” do akceptacji.
            migrationBuilder.AddColumn<bool>(
                name: "OffPackageAtTrainer",
                table: "TrainerConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "OffPackageNeedsApproval",
                table: "TrainerConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "OffPackageOnline",
                table: "TrainerConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "OffPackageUnpaidLimit",
                table: "TrainerConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresPackage",
                table: "SessionTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SinglePrice",
                table: "SessionTypes",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AwaitingApproval",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldUntil",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OffPackagePayment",
                table: "Sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidVia",
                table: "Sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrustedForDeferredPayment",
                table: "Clients",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OffPackageAtTrainer",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "OffPackageNeedsApproval",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "OffPackageOnline",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "OffPackageUnpaidLimit",
                table: "TrainerConfigs");

            migrationBuilder.DropColumn(
                name: "RequiresPackage",
                table: "SessionTypes");

            migrationBuilder.DropColumn(
                name: "SinglePrice",
                table: "SessionTypes");

            migrationBuilder.DropColumn(
                name: "AwaitingApproval",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "HoldUntil",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "OffPackagePayment",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PaidVia",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrustedForDeferredPayment",
                table: "Clients");
        }
    }
}
