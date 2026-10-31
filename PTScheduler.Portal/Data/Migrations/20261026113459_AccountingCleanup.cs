using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountingCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Excluded",
                table: "SalesLedger",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedReason",
                table: "SalesLedger",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SkipDocument",
                table: "SalesLedger",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AwaitingApproval",
                table: "SalesDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Excluded",
                table: "SalesLedger");

            migrationBuilder.DropColumn(
                name: "ExcludedReason",
                table: "SalesLedger");

            migrationBuilder.DropColumn(
                name: "SkipDocument",
                table: "SalesLedger");

            migrationBuilder.DropColumn(
                name: "AwaitingApproval",
                table: "SalesDocuments");
        }
    }
}
