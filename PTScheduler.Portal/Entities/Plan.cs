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

    // Stripe Price IDs (created in Stripe Dashboard, one per interval)
    public string? StripeMonthlyPriceId { get; set; }
    public string? StripeYearlyPriceId { get; set; }
    public int TrialDays { get; set; } = 14;

    // ── Limity ilościowe ─────────────────────────────────
    public int MaxClients { get; set; }
    public int MaxTrainers { get; set; }
    public int MaxSubordinates { get; set; }
    public int MaxCourses { get; set; }
    public int MaxSessionsPerMonth { get; set; }
    public int MaxStorageGB { get; set; } = 1;
    public int MaxVideoStorageGB { get; set; }
    public int MaxVideoBandwidthGBPerMonth { get; set; }
    public int MaxSmsPerMonth { get; set; }

    // ── Płatności ─────────────────────────────────
    public bool PaymentsEnabled { get; set; }
    public bool Coupons { get; set; }

    // ── Materiały ─────────────────────────────────
    public bool CoursesEnabled { get; set; }
    public bool BodyMeasurements { get; set; } = true;

    // ── Komunikacja ─────────────────────────────────
    public bool EmailReminders { get; set; } = true;
    public bool SmsReminders { get; set; }
    public bool PushNotifications { get; set; }

    // ── Grafik ─────────────────────────────────
    public bool RecurringSessions { get; set; } = true;

    // ── Uprawnienia i bezpieczeństwo ─────────────────────────────────
    public bool RoleBasedAccess { get; set; }
    public bool AuditLog { get; set; }
    public bool TwoFactorAuth { get; set; } = true;

    // ── Branding ─────────────────────────────────
    public bool CustomLogo { get; set; } = true;
    public bool CustomFavicon { get; set; } = true;
    public bool CustomEmailTemplates { get; set; }

    // ── Raporty i analityka ─────────────────────────────────
    public bool BasicAnalytics { get; set; } = true;
    public bool AdvancedAnalytics { get; set; }
    public bool FinancialReports { get; set; }
    public bool ClientReports { get; set; }
    public bool DataExport { get; set; }

    // ── Integracje płatności ─────────────────────────────────
    public bool IntegrationPayU { get; set; }
    public bool IntegrationPrzelewy24 { get; set; }

    // ── Wideo ─────────────────────────────────
    // none / youtube / bunny
    public string VideoProvider { get; set; } = "youtube";

    // ── Integracje rozmowy ─────────────────────────────────
    public bool IntegrationGoogleMeet { get; set; }

    public ICollection<Tenant> Tenants { get; set; } = [];
}
