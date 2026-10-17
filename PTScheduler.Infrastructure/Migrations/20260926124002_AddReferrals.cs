using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PTScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralCode",
                table: "Clients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MarketingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReferralEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReferrerRewardKind = table.Column<int>(type: "integer", nullable: false),
                    ReferrerRewardValue = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ReferrerRewardSessionTypeId = table.Column<int>(type: "integer", nullable: true),
                    FriendDiscountPercent = table.Column<int>(type: "integer", nullable: false),
                    MaxRewardsPerClient = table.Column<int>(type: "integer", nullable: false),
                    ReviewsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleReviewUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewAskAfterSessions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReferrerClientId = table.Column<int>(type: "integer", nullable: false),
                    ReferredClientId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RewardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RewardDescription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RewardCouponCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RewardPackageId = table.Column<int>(type: "integer", nullable: true),
                    FriendCouponCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Referrals_Clients_ReferredClientId",
                        column: x => x.ReferredClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Referrals_Clients_ReferrerClientId",
                        column: x => x.ReferrerClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_ReferralCode",
                table: "Clients",
                column: "ReferralCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferredClientId",
                table: "Referrals",
                column: "ReferredClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferrerClientId_Status",
                table: "Referrals",
                columns: new[] { "ReferrerClientId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketingSettings");

            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Clients_ReferralCode",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ReferralCode",
                table: "Clients");
        }
    }
}
