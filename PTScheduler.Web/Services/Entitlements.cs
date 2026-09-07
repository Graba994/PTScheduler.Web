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
    public int MaxCourses { get; set; }
    public int MaxSessionsPerMonth { get; set; } = int.MaxValue;
    public int MaxStorageGB { get; set; } = int.MaxValue;
    public int MaxVideoStorageGB { get; set; }
    public int MaxVideoBandwidthGBPerMonth { get; set; }
    public int MaxSmsPerMonth { get; set; }

    // ── Modules ─────────────────────────────────
    public bool PaymentsEnabled { get; set; }
    public bool Coupons { get; set; }

    public bool CoursesEnabled { get; set; }
    public bool BodyMeasurements { get; set; } = true;

    public bool EmailReminders { get; set; } = true;
    public bool SmsReminders { get; set; }
    public bool PushNotifications { get; set; }

    public bool RecurringSessions { get; set; } = true;

    public bool RoleBasedAccess { get; set; }
    public bool AuditLog { get; set; }
    public bool TwoFactorAuth { get; set; } = true;

    // "preview" | "basic" | "full" | "premium"
    public string BrandingTier { get; set; } = "preview";
    public bool CustomLogo { get; set; } = true;
    public bool CustomFavicon { get; set; } = true;
    public bool CustomEmailTemplates { get; set; }

    public bool BasicAnalytics { get; set; } = true;
    public bool AdvancedAnalytics { get; set; }
    public bool FinancialReports { get; set; }
    public bool ClientReports { get; set; }
    public bool DataExport { get; set; }

    // ── Integrations ─────────────────────────────────
    public bool IntegrationPayU { get; set; }
    public bool IntegrationPrzelewy24 { get; set; }
    public string VideoProvider { get; set; } = "youtube";
    public bool IntegrationGoogleMeet { get; set; }

    // Fallback used when TENANT_ENTITLEMENTS isn't set — legacy behavior,
    // no limits, no locked modules.
    public static Entitlements Unlimited() => new()
    {
        Id = "unlimited",
        Name = "Bez ograniczeń",
        BrandingTier = "premium",
        MaxClients = int.MaxValue,
        MaxCourses = int.MaxValue,
        MaxSessionsPerMonth = int.MaxValue,
        MaxStorageGB = int.MaxValue,
        MaxVideoStorageGB = int.MaxValue,
        MaxVideoBandwidthGBPerMonth = int.MaxValue,
        MaxSmsPerMonth = int.MaxValue,
        PaymentsEnabled = true,
        Coupons = true,
        CoursesEnabled = true,
        SmsReminders = true,
        PushNotifications = true,
        RoleBasedAccess = true,
        AuditLog = true,
        CustomEmailTemplates = true,
        AdvancedAnalytics = true,
        FinancialReports = true,
        ClientReports = true,
        DataExport = true,
        IntegrationPayU = true,
        IntegrationPrzelewy24 = true,
        IntegrationGoogleMeet = true,
        VideoProvider = "bunny"
    };
}
