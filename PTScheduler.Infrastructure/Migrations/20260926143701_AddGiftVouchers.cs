using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GiftVoucherId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoucherAmounts",
                table: "MarketingSettings",
                type: "text",
                nullable: false,
                defaultValue: "100,200,300,500");

            migrationBuilder.AddColumn<int>(
                name: "VoucherValidMonths",
                table: "MarketingSettings",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<bool>(
                name: "VouchersEnabled",
                table: "MarketingSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "GiftVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PackageOfferId = table.Column<int>(type: "integer", nullable: true),
                    SessionTypeId = table.Column<int>(type: "integer", nullable: true),
                    SessionsCount = table.Column<int>(type: "integer", nullable: true),
                    PackageValidDays = table.Column<int>(type: "integer", nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    FromName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BuyerUserId = table.Column<string>(type: "text", nullable: true),
                    IssuedManually = table.Column<bool>(type: "boolean", nullable: false),
                    IssuedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RedeemedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RedeemedByClientId = table.Column<int>(type: "integer", nullable: true),
                    CouponId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftVouchers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiftVouchers_BuyerUserId",
                table: "GiftVouchers",
                column: "BuyerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftVouchers_Code",
                table: "GiftVouchers",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiftVouchers");

            migrationBuilder.DropColumn(
                name: "GiftVoucherId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VoucherAmounts",
                table: "MarketingSettings");

            migrationBuilder.DropColumn(
                name: "VoucherValidMonths",
                table: "MarketingSettings");

            migrationBuilder.DropColumn(
                name: "VouchersEnabled",
                table: "MarketingSettings");
        }
    }
}
