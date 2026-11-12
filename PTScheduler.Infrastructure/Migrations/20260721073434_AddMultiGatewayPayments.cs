using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiGatewayPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProvidersJson",
                table: "PaymentSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CourseId",
                table: "Orders",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PackageOfferId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PackageOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    SessionTypeId = table.Column<int>(type: "integer", nullable: false),
                    SessionsCount = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    ValidDays = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageOffers_SessionTypes_SessionTypeId",
                        column: x => x.SessionTypeId,
                        principalTable: "SessionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PackageOfferId",
                table: "Orders",
                column: "PackageOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageOffers_SessionTypeId",
                table: "PackageOffers",
                column: "SessionTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_PackageOffers_PackageOfferId",
                table: "Orders",
                column: "PackageOfferId",
                principalTable: "PackageOffers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_PackageOffers_PackageOfferId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "PackageOffers");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PackageOfferId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ProvidersJson",
                table: "PaymentSettings");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PackageOfferId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Orders");

            migrationBuilder.AlterColumn<int>(
                name: "CourseId",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
