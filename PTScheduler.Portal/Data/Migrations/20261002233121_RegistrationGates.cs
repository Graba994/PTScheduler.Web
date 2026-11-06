using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingInterval",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "monthly");

            migrationBuilder.AddColumn<string>(
                name: "CustomDomain",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomDomainSince",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomDomainStatus",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueuedReason",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationKey",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CustomDomain",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StripeProductId",
                table: "Plans",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InviteCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ExtraTrialDays = table.Column<int>(type: "integer", nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: false),
                    Uses = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InviteCodes", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                columns: new[] { "CustomDomain", "StripeProductId" },
                values: new object[] { false, null });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                columns: new[] { "CustomDomain", "StripeProductId" },
                values: new object[] { false, null });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "starter",
                columns: new[] { "CustomDomain", "StripeProductId" },
                values: new object[] { false, null });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                columns: new[] { "CustomDomain", "StripeProductId" },
                values: new object[] { false, null });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_RegistrationKey",
                table: "Tenants",
                column: "RegistrationKey");

            migrationBuilder.CreateIndex(
                name: "IX_InviteCodes_Code",
                table: "InviteCodes",
                column: "Code",
                unique: true);

            // Własna domena domyślnie w płatnych planach.
            migrationBuilder.Sql("UPDATE \"Plans\" SET \"CustomDomain\" = TRUE WHERE \"MonthlyPrice\" > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InviteCodes");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_RegistrationKey",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BillingInterval",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CustomDomain",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CustomDomainSince",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CustomDomainStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "InviteCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "QueuedReason",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "RegistrationKey",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CustomDomain",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "StripeProductId",
                table: "Plans");
        }
    }
}
