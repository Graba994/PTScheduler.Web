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
    /// <summary>Część limitów wideo pochodząca z dodatków miesięczych (już wliczona w Max…).</summary>
    public int AddonVideoBandwidthGB { get; set; }
    public int AddonVideoStorageGB { get; set; }
    public int MaxSmsPerMonth { get; set; }

    // ── Modules ─────────────────────────────────
    public bool PaymentsEnabled { get; set; }
    public bool Coupons { get; set; }

    public bool CoursesEnabled { get; set; }

    // Moduł treningowy: katalog ćwiczeń, plany, trening klienta, postępy.
    // Domyślnie true: JSON planu sprzed wprowadzenia tej flagi nie ma klucza,
    // a nie odbieramy modułu tenantom, zanim portal wyśle aktualne uprawnienia
    // (przy zapisie planu, zmianie planu i starcie tenanta).
    public bool TrainingPlansEnabled { get; set; } = true;
    public bool BodyMeasurements { get; set; } = true;

    public bool EmailReminders { get; set; } = true;
    public bool SmsReminders { get; set; }
    /// <summary>Platforma ma bramkę SMS (Portal: token SMSAPI + włącznik). Bez niej nikt nie widzi opcji SMS — także zajawek.</summary>
    public bool SmsAvailable { get; set; } = true;
    public bool PushNotifications { get; set; }

    public bool RecurringSessions { get; set; } = true;

    public bool RoleBasedAccess { get; set; }
    public bool AuditLog { get; set; }
    public bool TwoFactorAuth { get; set; } = true;

    // "preview" | "basic" | "full" | "premium"
    public string BrandingTier { get; set; } = "preview";
    public bool CustomLogo { get; set; } = true;
    public bool CustomFavicon { get; set; } = true;
    public bool CustomDomain { get; set; }
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

    // ── Marketing ─────────────────────────────────
    // Program poleceń: domyślnie wyłączony — to płatny dodatek w wybranych planach.
    public bool ReferralProgram { get; set; }

    // ── Sprzedaż, klienci i AI (od planów 2026-11) ─────────────────────────────────
    // Domyślnie włączone — starszy Portal ich nie wysyła, a wcześniej były w każdym planie.
    public bool SalesTools { get; set; } = true;
    public bool Surveys { get; set; } = true;
    public bool Automations { get; set; } = true;
    public bool Documents { get; set; } = true;
    public bool AiAssistant { get; set; } = true;
    public int MaxAiActionsPerMonth { get; set; } = int.MaxValue;
    public bool WhiteLabel { get; set; }
    public bool PrioritySupport { get; set; }

    /// <summary>Najtańszy plan z daną funkcją (klucz = pole uprawnień) — nazwa przy kłódce.</summary>
    public Dictionary<string, string> UpgradePlans { get; set; } = [];
    /// <summary>Funkcje, których bez planu w ogóle nie pokazujemy (zamiast zajawki) — ustawiane w panelu planów.</summary>
    public List<string> HiddenWhenLocked { get; set; } = [];
    /// <summary>Okres próbny wyższego planu (np. Pro na start) — do kiedy i jaki plan zostaje potem.</summary>
    public DateTime? TrialEndsAt { get; set; }
    public string? BasePlanName { get; set; }

    // ── Zegarki klientów ─────────────────────────────────
    // Garmin u klientów: plan ma „Zegarki klientów” i Garmin jest włączony globalnie w Portalu.
    public bool Wearables { get; set; }

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
        ReferralProgram = true,
        VideoProvider = "bunny"
    };
}
