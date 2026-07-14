using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHiddenFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "SessionPackages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FinancePins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PinHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancePins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceTaxConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Module = table.Column<string>(type: "text", nullable: false),
                    VatEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    IncomeTaxType = table.Column<string>(type: "text", nullable: false),
                    FlatTaxRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    LumpSumRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ScaleTaxThreshold = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ScaleTaxRateLow = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ScaleTaxRateHigh = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ZusEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ZusMonthlyAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    HealthInsuranceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    HealthInsuranceMonthly = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CostDeductionsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyFixedCosts = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    InvoiceNumberingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    InvoicePrefix = table.Column<string>(type: "text", nullable: false),
                    InvoiceNextNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceTaxConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTaxConfigs_Module",
                table: "FinanceTaxConfigs",
                column: "Module",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FinanceTaxConfigs");
            migrationBuilder.DropTable(name: "FinancePins");
            migrationBuilder.DropColumn(name: "IsHidden", table: "SessionPackages");
        }
    }
}
