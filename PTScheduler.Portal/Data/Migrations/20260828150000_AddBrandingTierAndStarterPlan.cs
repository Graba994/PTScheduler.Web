using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingTierAndStarterPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandingTier",
                table: "Plans",
                type: "text",
                nullable: false,
                defaultValue: "preview");

            // ── Update existing plans ──

            // Start: free trial, branding preview only
            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                columns: new[] { "Description", "TrialDays", "BrandingTier", "CustomLogo", "CustomFavicon" },
                values: new object[] { "Testuj za darmo przez 7 dni", 7, "preview", false, false });

            // Pro: price bump, full branding
            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                columns: new[] { "MonthlyPrice", "YearlyPrice", "BrandingTier", "SortOrder" },
                values: new object[] { 99m, 990m, "full", 3 });

            // Studio → Business: price bump, premium branding
            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                columns: new[] { "Name", "MonthlyPrice", "YearlyPrice", "BrandingTier", "SortOrder" },
                values: new object[] { "Business", 199m, 1990m, "premium", 4 });

            // ── Insert new Starter plan ──
            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[]
                {
                    "Id", "Name", "Description", "MonthlyPrice", "YearlyPrice", "Currency",
                    "SortOrder", "IsActive", "IsFeatured", "TrialDays",
                    "MaxClients", "MaxTrainers", "MaxSubordinates", "MaxCourses",
                    "MaxSessionsPerMonth", "MaxStorageGB", "MaxVideoStorageGB",
                    "MaxVideoBandwidthGBPerMonth", "MaxSmsPerMonth",
                    "PaymentsEnabled", "Coupons", "CoursesEnabled", "BodyMeasurements",
                    "EmailReminders", "SmsReminders", "PushNotifications",
                    "RecurringSessions", "RoleBasedAccess", "AuditLog", "TwoFactorAuth",
                    "BrandingTier", "CustomLogo", "CustomFavicon", "CustomEmailTemplates",
                    "BasicAnalytics", "AdvancedAnalytics", "FinancialReports",
                    "ClientReports", "DataExport",
                    "IntegrationPayU", "IntegrationPrzelewy24", "IntegrationGoogleMeet",
                    "VideoProvider",
                    "StripeMonthlyPriceId", "StripeYearlyPriceId"
                },
                values: new object[]
                {
                    "starter", "Starter", "Podstawowe narzędzia dla trenera", 49m, 490m, "PLN",
                    2, true, false, 14,
                    15, 0, 0, 3,
                    200, 5, 5,
                    0, 0,
                    true, false, false, true,
                    true, false, false,
                    true, false, false, true,
                    "basic", true, true, false,
                    true, false, true,
                    false, false,
                    true, false, false,
                    "youtube",
                    null, null
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "starter");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                columns: new[] { "Description", "TrialDays", "CustomLogo", "CustomFavicon" },
                values: new object[] { "Dla trenera, który zaczyna", 14, true, true });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                columns: new[] { "MonthlyPrice", "YearlyPrice", "SortOrder" },
                values: new object[] { 79m, 790m, 2 });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                columns: new[] { "Name", "MonthlyPrice", "YearlyPrice", "SortOrder" },
                values: new object[] { "Studio", 149m, 1490m, 3 });

            migrationBuilder.DropColumn(
                name: "BrandingTier",
                table: "Plans");
        }
    }
}
