using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PairTrainings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPair",
                table: "SessionTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PairGroupId",
                table: "Sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SharesPackageSlot",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PartnerClientId",
                table: "SessionPackages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsForPair",
                table: "PackageOffers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AppBaseUrl",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartnerClientId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PairGroupId",
                table: "Sessions",
                column: "PairGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPackages_PartnerClientId",
                table: "SessionPackages",
                column: "PartnerClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_SessionPackages_Clients_PartnerClientId",
                table: "SessionPackages",
                column: "PartnerClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SessionPackages_Clients_PartnerClientId",
                table: "SessionPackages");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_PairGroupId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_SessionPackages_PartnerClientId",
                table: "SessionPackages");

            migrationBuilder.DropColumn(
                name: "IsPair",
                table: "SessionTypes");

            migrationBuilder.DropColumn(
                name: "PairGroupId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SharesPackageSlot",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PartnerClientId",
                table: "SessionPackages");

            migrationBuilder.DropColumn(
                name: "IsForPair",
                table: "PackageOffers");

            migrationBuilder.DropColumn(
                name: "AppBaseUrl",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PartnerClientId",
                table: "Orders");
        }
    }
}
