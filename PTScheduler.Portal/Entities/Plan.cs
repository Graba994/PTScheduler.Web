namespace PTScheduler.Portal.Entities;

public class Plan
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public string Currency { get; set; } = "PLN";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }

    // ── Limity ilościowe ─────────────────────────────────
    public int MaxClients { get; set; }
    public int MaxTrainers { get; set; }
    public int MaxSubordinates { get; set; }
    public int MaxLocations { get; set; } = 1;
    public int MaxCourses { get; set; }
    public int MaxSessionsPerMonth { get; set; }
    public int MaxStorageGB { get; set; } = 1;
    public int MaxVideoStorageGB { get; set; }
    public int MaxVideoBandwidthGBPerMonth { get; set; }
    public int MaxEmailsPerMonth { get; set; } = 100;
    public int MaxSmsPerMonth { get; set; }
    public int MaxPushNotificationsPerDay { get; set; } = 10;
    public int BackupRetentionDays { get; set; } = 7;

    // ── Płatności ─────────────────────────────────
    public bool PaymentsEnabled { get; set; }
    public bool MultipleGateways { get; set; }
    public bool Invoicing { get; set; }
    public bool TaxReports { get; set; }
    public bool Coupons { get; set; }
    public bool Subscriptions { get; set; }

    // ── Materiały ─────────────────────────────────
    public bool CoursesEnabled { get; set; }
    public bool WorkoutPlansEnabled { get; set; }
    public bool MealPlansEnabled { get; set; }
    public bool ClientDocuments { get; set; }
    public bool ProgressPhotos { get; set; }
    public bool BodyMeasurements { get; set; }
    public bool NutritionTracking { get; set; }

    // ── Komunikacja ─────────────────────────────────
    public bool ClientChat { get; set; }
    public bool VideoCalls { get; set; }
    public bool EmailReminders { get; set; } = true;
    public bool SmsReminders { get; set; }
    public bool PushNotifications { get; set; }
    public bool AutomatedFollowups { get; set; }
    public bool GroupMessaging { get; set; }

    // ── Grafik ─────────────────────────────────
    public bool GroupSessions { get; set; }
    public bool RecurringSessions { get; set; }
    public bool Waitlist { get; set; }
    public bool MultipleCalendars { get; set; }
    public bool CalendarSync { get; set; }

    // ── Uprawnienia i bezpieczeństwo ─────────────────────────────────
    public bool RoleBasedAccess { get; set; }
    public bool AuditLog { get; set; }
    public bool TwoFactorAuth { get; set; }

    // ── Branding ─────────────────────────────────
    public bool CustomLogo { get; set; } = true;
    public bool CustomColors { get; set; }
    public bool CustomFavicon { get; set; }
    public bool CustomDomain { get; set; }
    public bool RemoveWatermark { get; set; }
    public bool CustomEmailTemplates { get; set; }
    public bool MultiLanguage { get; set; }
    public bool LandingPageBuilder { get; set; }
    public bool BlogModule { get; set; }

    // ── Raporty i analityka ─────────────────────────────────
    public bool BasicAnalytics { get; set; } = true;
    public bool AdvancedAnalytics { get; set; }
    public bool FinancialReports { get; set; }
    public bool ClientReports { get; set; }
    public bool DataExport { get; set; }

    // ── AI ─────────────────────────────────
    public bool AiWorkoutGenerator { get; set; }
    public bool AiMealPlanner { get; set; }
    public bool AiChatbot { get; set; }
    public bool AiCallTranscription { get; set; }

    // ── Wsparcie ─────────────────────────────────
    public bool EmailSupport { get; set; } = true;
    public bool PrioritySupport { get; set; }
    public bool PhoneSupport { get; set; }
    public bool DedicatedAccountManager { get; set; }
    public bool OnboardingCall { get; set; }
    public bool SlaGuarantee { get; set; }

    // ── Integracje płatności ─────────────────────────────────
    public bool IntegrationPayU { get; set; }
    public bool IntegrationPrzelewy24 { get; set; }
    public bool IntegrationStripe { get; set; }
    public bool IntegrationAutoPay { get; set; }
    public bool IntegrationKlarna { get; set; }
    public bool IntegrationBlik { get; set; }

    // ── Wideo ─────────────────────────────────
    // none / youtube / bunny / vimeo
    public string VideoProvider { get; set; } = "youtube";

    // ── Integracje kalendarz i rozmowy ─────────────────────────────────
    public bool IntegrationGoogleCalendar { get; set; }
    public bool IntegrationOutlookCalendar { get; set; }
    public bool IntegrationAppleCalendar { get; set; }
    public bool IntegrationZoom { get; set; }
    public bool IntegrationGoogleMeet { get; set; }
    public bool IntegrationTeams { get; set; }

    // ── Marketing i automatyzacja ─────────────────────────────────
    public bool IntegrationMailerLite { get; set; }
    public bool IntegrationMailchimp { get; set; }
    public bool IntegrationZapier { get; set; }
    public bool IntegrationMake { get; set; }
    public bool IntegrationCustomWebhooks { get; set; }

    // ── Powiadomienia zewnętrzne ─────────────────────────────────
    public bool IntegrationSlack { get; set; }
    public bool IntegrationTelegram { get; set; }
    public bool IntegrationWhatsApp { get; set; }
    public bool IntegrationDiscord { get; set; }

    // ── Analityka zewnętrzna ─────────────────────────────────
    public bool IntegrationGoogleAnalytics { get; set; }
    public bool IntegrationMetaPixel { get; set; }

    public ICollection<Tenant> Tenants { get; set; } = [];
}
