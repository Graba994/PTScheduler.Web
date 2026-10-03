using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class StripeSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingStatus",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StripeCheckoutSessionId",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeMonthlyPriceId",
                table: "Plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeYearlyPriceId",
                table: "Plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrialDays",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                columns: new[] { "StripeMonthlyPriceId", "StripeYearlyPriceId", "TrialDays" },
                values: new object[] { null, null, 14 });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                columns: new[] { "StripeMonthlyPriceId", "StripeYearlyPriceId", "TrialDays" },
                values: new object[] { null, null, 14 });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                columns: new[] { "StripeMonthlyPriceId", "StripeYearlyPriceId", "TrialDays" },
                values: new object[] { null, null, 14 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "StripeCheckoutSessionId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "StripeMonthlyPriceId",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "StripeYearlyPriceId",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "TrialDays",
                table: "Plans");
        }
    }
}
