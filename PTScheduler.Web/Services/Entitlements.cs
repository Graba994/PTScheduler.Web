namespace PTScheduler.Web.Services;

// Deserialized from TENANT_ENTITLEMENTS env var (set by the portal).
// Property names must match camelCase JSON emitted by the portal's
// TenantService.SerializePlan.
public class Entitlements
{
    public string Id { get; set; } = "unknown";
    public string Name { get; set; } = "Nieznany";
    public decimal MonthlyPrice { get; set; }

    // ── Limits ─────────────────────────────────
    public int MaxClients { get; set; } = int.MaxValue;
    public int MaxTrainers { get; set; }
    public int MaxSubordinates { get; set; }
    public int MaxLocations { get; set; } = 1;
    public int MaxCourses { get; set; }
    public int MaxSessionsPerMonth { get; set; } = int.MaxValue;
    public int MaxStorageGB { get; set; } = int.MaxValue;
    public int MaxVideoStorageGB { get; set; }
    public int MaxVideoBandwidthGBPerMonth { get; set; }
    public int MaxEmailsPerMonth { get; set; } = int.MaxValue;
    public int MaxSmsPerMonth { get; set; }
    public int MaxPushNotificationsPerDay { get; set; } = int.MaxValue;
    public int BackupRetentionDays { get; set; } = 7;

    // ── Modules ─────────────────────────────────
    public bool PaymentsEnabled { get; set; }
    public bool MultipleGateways { get; set; }
    public bool Invoicing { get; set; }
    public bool TaxReports { get; set; }
    public bool Coupons { get; set; }
    public bool Subscriptions { get; set; }

    public bool CoursesEnabled { get; set; }
    public bool WorkoutPlansEnabled { get; set; } = true;
    public bool MealPlansEnabled { get; set; }
    public bool ClientDocuments { get; set; } = true;
    public bool ProgressPhotos { get; set; }
    public bool BodyMeasurements { get; set; } = true;
    public bool NutritionTracking { get; set; }

    public bool ClientChat { get; set; }
    public bool VideoCalls { get; set; }
    public bool EmailReminders { get; set; } = true;
    public bool SmsReminders { get; set; }
    public bool PushNotifications { get; set; }
    public bool AutomatedFollowups { get; set; }
    public bool GroupMessaging { get; set; }

    public bool GroupSessions { get; set; }
    public bool RecurringSessions { get; set; } = true;
    public bool Waitlist { get; set; }
    public bool MultipleCalendars { get; set; }
    public bool CalendarSync { get; set; }

    public bool RoleBasedAccess { get; set; }
    public bool AuditLog { get; set; }
    public bool TwoFactorAuth { get; set; }

    public bool CustomLogo { get; set; } = true;
    public bool CustomColors { get; set; }
    public bool CustomFavicon { get; set; }
    public bool CustomDomain { get; set; }
    public bool RemoveWatermark { get; set; }
    public bool CustomEmailTemplates { get; set; }
    public bool MultiLanguage { get; set; }
    public bool LandingPageBuilder { get; set; }
    public bool BlogModule { get; set; }

    public bool BasicAnalytics { get; set; } = true;
    public bool AdvancedAnalytics { get; set; }
    public bool FinancialReports { get; set; }
    public bool ClientReports { get; set; }
    public bool DataExport { get; set; }

    public bool AiWorkoutGenerator { get; set; }
    public bool AiMealPlanner { get; set; }
    public bool AiChatbot { get; set; }
    public bool AiCallTranscription { get; set; }

    public bool EmailSupport { get; set; } = true;
    public bool PrioritySupport { get; set; }
    public bool PhoneSupport { get; set; }
    public bool DedicatedAccountManager { get; set; }
    public bool OnboardingCall { get; set; }
    public bool SlaGuarantee { get; set; }

    // ── Integrations ─────────────────────────────────
    public bool IntegrationPayU { get; set; }
    public bool IntegrationPrzelewy24 { get; set; }
    public bool IntegrationStripe { get; set; }
    public bool IntegrationAutoPay { get; set; }
    public bool IntegrationKlarna { get; set; }
    public bool IntegrationBlik { get; set; }
    public string VideoProvider { get; set; } = "youtube";
    public bool IntegrationGoogleCalendar { get; set; }
    public bool IntegrationOutlookCalendar { get; set; }
    public bool IntegrationAppleCalendar { get; set; }
    public bool IntegrationZoom { get; set; }
    public bool IntegrationGoogleMeet { get; set; }
    public bool IntegrationTeams { get; set; }
    public bool IntegrationMailerLite { get; set; }
    public bool IntegrationMailchimp { get; set; }
    public bool IntegrationZapier { get; set; }
    public bool IntegrationMake { get; set; }
    public bool IntegrationCustomWebhooks { get; set; }
    public bool IntegrationSlack { get; set; }
    public bool IntegrationTelegram { get; set; }
    public bool IntegrationWhatsApp { get; set; }
    public bool IntegrationDiscord { get; set; }
    public bool IntegrationGoogleAnalytics { get; set; }
    public bool IntegrationMetaPixel { get; set; }

    // Fallback used when TENANT_ENTITLEMENTS isn't set — legacy behavior,
    // no limits, no locked modules.
    public static Entitlements Unlimited() => new()
    {
        Id = "unlimited",
        Name = "Bez ograniczeń",
        MaxClients = int.MaxValue,
        MaxCourses = int.MaxValue,
        MaxSessionsPerMonth = int.MaxValue,
        MaxStorageGB = int.MaxValue,
        MaxVideoStorageGB = int.MaxValue,
        MaxVideoBandwidthGBPerMonth = int.MaxValue,
        MaxEmailsPerMonth = int.MaxValue,
        MaxSmsPerMonth = int.MaxValue,
        MaxPushNotificationsPerDay = int.MaxValue,
        PaymentsEnabled = true,
        CoursesEnabled = true,
        CustomDomain = true,
        RemoveWatermark = true,
        MealPlansEnabled = true,
        ProgressPhotos = true,
        NutritionTracking = true,
        ClientChat = true,
        VideoCalls = true,
        SmsReminders = true,
        PushNotifications = true,
        AutomatedFollowups = true,
        GroupMessaging = true,
        GroupSessions = true,
        Waitlist = true,
        MultipleCalendars = true,
        CalendarSync = true,
        RoleBasedAccess = true,
        AuditLog = true,
        TwoFactorAuth = true,
        CustomColors = true,
        CustomFavicon = true,
        CustomEmailTemplates = true,
        MultiLanguage = true,
        LandingPageBuilder = true,
        BlogModule = true,
        AdvancedAnalytics = true,
        FinancialReports = true,
        ClientReports = true,
        DataExport = true,
        Invoicing = true,
        TaxReports = true,
        Coupons = true,
        Subscriptions = true,
        MultipleGateways = true,
        AiWorkoutGenerator = true,
        AiMealPlanner = true,
        AiChatbot = true,
        AiCallTranscription = true,
        PrioritySupport = true,
        PhoneSupport = true,
        DedicatedAccountManager = true,
        OnboardingCall = true,
        SlaGuarantee = true
    };
}
