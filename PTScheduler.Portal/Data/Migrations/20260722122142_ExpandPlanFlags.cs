using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTScheduler.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPlanFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AdvancedAnalytics",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiCallTranscription",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiChatbot",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiMealPlanner",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiWorkoutGenerator",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AuditLog",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutomatedFollowups",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BackupRetentionDays",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BasicAnalytics",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BlogModule",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BodyMeasurements",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CalendarSync",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ClientChat",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ClientDocuments",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ClientReports",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Coupons",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CustomColors",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CustomEmailTemplates",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CustomFavicon",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CustomLogo",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DataExport",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DedicatedAccountManager",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EmailReminders",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EmailSupport",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FinancialReports",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GroupMessaging",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GroupSessions",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationAppleCalendar",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationAutoPay",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationBlik",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationCustomWebhooks",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationDiscord",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationGoogleAnalytics",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationGoogleCalendar",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationGoogleMeet",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationKlarna",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationMailchimp",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationMailerLite",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationMake",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationMetaPixel",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationOutlookCalendar",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationPayU",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationPrzelewy24",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationSlack",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationStripe",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationTeams",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationTelegram",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationWhatsApp",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationZapier",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntegrationZoom",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Invoicing",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LandingPageBuilder",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxCourses",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxEmailsPerMonth",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxLocations",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxPushNotificationsPerDay",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxSessionsPerMonth",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxSmsPerMonth",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxStorageGB",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxSubordinates",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxTrainers",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxVideoBandwidthGBPerMonth",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxVideoStorageGB",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MealPlansEnabled",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MultiLanguage",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MultipleCalendars",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MultipleGateways",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NutritionTracking",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OnboardingCall",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PhoneSupport",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProgressPhotos",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PushNotifications",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RecurringSessions",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RemoveWatermark",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RoleBasedAccess",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SlaGuarantee",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsReminders",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Subscriptions",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TaxReports",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorAuth",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "VideoCalls",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VideoProvider",
                table: "Plans",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Waitlist",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WorkoutPlansEnabled",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "pro",
                columns: new[] { "AdvancedAnalytics", "AiCallTranscription", "AiChatbot", "AiMealPlanner", "AiWorkoutGenerator", "AuditLog", "AutomatedFollowups", "BackupRetentionDays", "BasicAnalytics", "BlogModule", "BodyMeasurements", "CalendarSync", "ClientChat", "ClientDocuments", "ClientReports", "Coupons", "CustomColors", "CustomEmailTemplates", "CustomFavicon", "CustomLogo", "DataExport", "DedicatedAccountManager", "EmailReminders", "EmailSupport", "FinancialReports", "GroupMessaging", "GroupSessions", "IntegrationAppleCalendar", "IntegrationAutoPay", "IntegrationBlik", "IntegrationCustomWebhooks", "IntegrationDiscord", "IntegrationGoogleAnalytics", "IntegrationGoogleCalendar", "IntegrationGoogleMeet", "IntegrationKlarna", "IntegrationMailchimp", "IntegrationMailerLite", "IntegrationMake", "IntegrationMetaPixel", "IntegrationOutlookCalendar", "IntegrationPayU", "IntegrationPrzelewy24", "IntegrationSlack", "IntegrationStripe", "IntegrationTeams", "IntegrationTelegram", "IntegrationWhatsApp", "IntegrationZapier", "IntegrationZoom", "Invoicing", "LandingPageBuilder", "MaxCourses", "MaxEmailsPerMonth", "MaxLocations", "MaxPushNotificationsPerDay", "MaxSessionsPerMonth", "MaxSmsPerMonth", "MaxStorageGB", "MaxSubordinates", "MaxTrainers", "MaxVideoBandwidthGBPerMonth", "MaxVideoStorageGB", "MealPlansEnabled", "MultiLanguage", "MultipleCalendars", "MultipleGateways", "NutritionTracking", "OnboardingCall", "PhoneSupport", "ProgressPhotos", "PushNotifications", "RecurringSessions", "RemoveWatermark", "RoleBasedAccess", "SlaGuarantee", "SmsReminders", "Subscriptions", "TaxReports", "TwoFactorAuth", "VideoCalls", "VideoProvider", "Waitlist", "WorkoutPlansEnabled" },
                values: new object[] { true, false, false, false, false, false, true, 30, true, false, true, true, true, true, true, true, true, true, true, true, true, false, true, true, true, false, true, false, false, false, false, false, false, true, true, false, false, false, false, false, false, true, true, false, true, false, false, false, false, true, true, true, 10, 2000, 1, 10, 500, 50, 10, 1, 2, 100, 20, true, false, false, false, true, false, false, true, true, true, false, false, false, true, true, false, false, true, "bunny", true, true });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "start",
                columns: new[] { "AdvancedAnalytics", "AiCallTranscription", "AiChatbot", "AiMealPlanner", "AiWorkoutGenerator", "AuditLog", "AutomatedFollowups", "BackupRetentionDays", "BasicAnalytics", "BlogModule", "BodyMeasurements", "CalendarSync", "ClientChat", "ClientDocuments", "ClientReports", "Coupons", "CustomColors", "CustomEmailTemplates", "CustomFavicon", "CustomLogo", "DataExport", "DedicatedAccountManager", "EmailReminders", "EmailSupport", "FinancialReports", "GroupMessaging", "GroupSessions", "IntegrationAppleCalendar", "IntegrationAutoPay", "IntegrationBlik", "IntegrationCustomWebhooks", "IntegrationDiscord", "IntegrationGoogleAnalytics", "IntegrationGoogleCalendar", "IntegrationGoogleMeet", "IntegrationKlarna", "IntegrationMailchimp", "IntegrationMailerLite", "IntegrationMake", "IntegrationMetaPixel", "IntegrationOutlookCalendar", "IntegrationPayU", "IntegrationPrzelewy24", "IntegrationSlack", "IntegrationStripe", "IntegrationTeams", "IntegrationTelegram", "IntegrationWhatsApp", "IntegrationZapier", "IntegrationZoom", "Invoicing", "LandingPageBuilder", "MaxCourses", "MaxEmailsPerMonth", "MaxLocations", "MaxPushNotificationsPerDay", "MaxSessionsPerMonth", "MaxSmsPerMonth", "MaxStorageGB", "MaxSubordinates", "MaxTrainers", "MaxVideoBandwidthGBPerMonth", "MaxVideoStorageGB", "MealPlansEnabled", "MultiLanguage", "MultipleCalendars", "MultipleGateways", "NutritionTracking", "OnboardingCall", "PhoneSupport", "ProgressPhotos", "PushNotifications", "RecurringSessions", "RemoveWatermark", "RoleBasedAccess", "SlaGuarantee", "SmsReminders", "Subscriptions", "TaxReports", "TwoFactorAuth", "VideoCalls", "VideoProvider", "Waitlist", "WorkoutPlansEnabled" },
                values: new object[] { false, false, false, false, false, false, false, 7, true, false, true, false, false, true, false, false, false, false, false, true, false, false, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, 0, 100, 1, 10, 30, 0, 1, 0, 0, 0, 0, false, false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false, "youtube", false, false });

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                columns: new[] { "AdvancedAnalytics", "AiCallTranscription", "AiChatbot", "AiMealPlanner", "AiWorkoutGenerator", "AuditLog", "AutomatedFollowups", "BackupRetentionDays", "BasicAnalytics", "BlogModule", "BodyMeasurements", "CalendarSync", "ClientChat", "ClientDocuments", "ClientReports", "Coupons", "CustomColors", "CustomEmailTemplates", "CustomFavicon", "CustomLogo", "DataExport", "DedicatedAccountManager", "Description", "EmailReminders", "EmailSupport", "FinancialReports", "GroupMessaging", "GroupSessions", "IntegrationAppleCalendar", "IntegrationAutoPay", "IntegrationBlik", "IntegrationCustomWebhooks", "IntegrationDiscord", "IntegrationGoogleAnalytics", "IntegrationGoogleCalendar", "IntegrationGoogleMeet", "IntegrationKlarna", "IntegrationMailchimp", "IntegrationMailerLite", "IntegrationMake", "IntegrationMetaPixel", "IntegrationOutlookCalendar", "IntegrationPayU", "IntegrationPrzelewy24", "IntegrationSlack", "IntegrationStripe", "IntegrationTeams", "IntegrationTelegram", "IntegrationWhatsApp", "IntegrationZapier", "IntegrationZoom", "Invoicing", "LandingPageBuilder", "MaxCourses", "MaxEmailsPerMonth", "MaxLocations", "MaxPushNotificationsPerDay", "MaxSessionsPerMonth", "MaxSmsPerMonth", "MaxStorageGB", "MaxSubordinates", "MaxTrainers", "MaxVideoBandwidthGBPerMonth", "MaxVideoStorageGB", "MealPlansEnabled", "MultiLanguage", "MultipleCalendars", "MultipleGateways", "NutritionTracking", "OnboardingCall", "PhoneSupport", "ProgressPhotos", "PushNotifications", "RecurringSessions", "RemoveWatermark", "RoleBasedAccess", "SlaGuarantee", "SmsReminders", "Subscriptions", "TaxReports", "TwoFactorAuth", "VideoCalls", "VideoProvider", "Waitlist", "WorkoutPlansEnabled" },
                values: new object[] { true, false, true, true, true, true, true, 90, true, true, true, true, true, true, true, true, true, true, true, true, true, false, "Bez limitów, własna domena, priorytetowe wsparcie, AI", true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, 2147483647, 2147483647, 5, 10, 2147483647, 500, 100, 5, 10, 1000, 200, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, "bunny", true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvancedAnalytics",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AiCallTranscription",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AiChatbot",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AiMealPlanner",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AiWorkoutGenerator",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AuditLog",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AutomatedFollowups",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "BackupRetentionDays",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "BasicAnalytics",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "BlogModule",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "BodyMeasurements",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CalendarSync",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ClientChat",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ClientDocuments",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ClientReports",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Coupons",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CustomColors",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CustomEmailTemplates",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CustomFavicon",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CustomLogo",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "DataExport",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "DedicatedAccountManager",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "EmailReminders",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "EmailSupport",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "FinancialReports",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "GroupMessaging",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "GroupSessions",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationAppleCalendar",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationAutoPay",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationBlik",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationCustomWebhooks",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationDiscord",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationGoogleAnalytics",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationGoogleCalendar",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationGoogleMeet",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationKlarna",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationMailchimp",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationMailerLite",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationMake",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationMetaPixel",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationOutlookCalendar",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationPayU",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationPrzelewy24",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationSlack",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationStripe",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationTeams",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationTelegram",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationWhatsApp",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationZapier",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "IntegrationZoom",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Invoicing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "LandingPageBuilder",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxCourses",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxEmailsPerMonth",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxLocations",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxPushNotificationsPerDay",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxSessionsPerMonth",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxSmsPerMonth",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxStorageGB",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxSubordinates",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxTrainers",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxVideoBandwidthGBPerMonth",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxVideoStorageGB",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MealPlansEnabled",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MultiLanguage",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MultipleCalendars",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MultipleGateways",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "NutritionTracking",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "OnboardingCall",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PhoneSupport",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "ProgressPhotos",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PushNotifications",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "RecurringSessions",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "RemoveWatermark",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "RoleBasedAccess",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SlaGuarantee",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SmsReminders",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Subscriptions",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "TaxReports",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "TwoFactorAuth",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "VideoCalls",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "VideoProvider",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Waitlist",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "WorkoutPlansEnabled",
                table: "Plans");

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: "studio",
                column: "Description",
                value: "Bez limitów, własna domena, priorytetowe wsparcie");
        }
    }
}
