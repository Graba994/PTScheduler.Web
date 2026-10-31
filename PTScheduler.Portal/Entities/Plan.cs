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
    /// <summary>Produkt w Stripe — Portal zakłada go sam razem z cenami (StripeService.EnsurePricesAsync).</summary>
    public string? StripeProductId { get; set; }
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
    public bool TrainingPlansEnabled { get; set; }
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
    // "preview" | "basic" | "full" | "premium"
    public string BrandingTier { get; set; } = "preview";
    public bool CustomLogo { get; set; } = true;
    /// <summary>Własna domena trenera (np. annafit.pl) obok adresu w domenie platformy.</summary>
    public bool CustomDomain { get; set; }
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
    /// <summary>Mapa mięśni przy ćwiczeniach: none / basic (ogólna, 17 partii) / detailed (szczegółowa — przód/bok/tył barków itd.).</summary>
    public string MuscleMapLevel { get; set; } = "detailed";

    // ── Integracje rozmowy ─────────────────────────────────
    public bool IntegrationGoogleMeet { get; set; }

    // ── Marketing ─────────────────────────────────
    public bool ReferralProgram { get; set; }

    // ── Zegarki klientów ─────────────────────────────────
    /// <summary>Garmin Connect u klientów (gotowość przed wizytą, aktywności). Działa, gdy Garmin jest też włączony globalnie w Portalu.</summary>
    public bool Wearables { get; set; }

    // ── Sprzedaż, klienci i AI (od planów 2026-11) ─────────────────────────────────
    /// <summary>Sklep dla klientów: karnety cykliczne i vouchery (pakiety treningów są w każdym planie).</summary>
    public bool SalesTools { get; set; }
    /// <summary>Ankiety dla klientów (poza ankietą zdrowotną przed pierwszym treningiem).</summary>
    public bool Surveys { get; set; }
    /// <summary>Automatyzacje (powitanie, powroty, urodziny) i retencja klientów.</summary>
    public bool Automations { get; set; }
    /// <summary>Dokumenty do podpisu przez klientów.</summary>
    public bool Documents { get; set; }
    /// <summary>Asystent AI: generator planów, poranny brief, szkice wiadomości.</summary>
    public bool AiAssistant { get; set; }
    /// <summary>Akcje AI w miesiącu (0 = bez AI, int.MaxValue = bez limitu).</summary>
    public int MaxAiActionsPerMonth { get; set; }
    /// <summary>Bez marki Hantly u klientów (white-label).</summary>
    public bool WhiteLabel { get; set; }
    /// <summary>Priorytetowe wsparcie i rozmowa wdrożeniowa.</summary>
    public bool PrioritySupport { get; set; }

    // Nigdy nie trafia do JSON-a uprawnień wysyłanego tenantom.
    [System.Text.Json.Serialization.JsonIgnore]
    public ICollection<Tenant> Tenants { get; set; } = [];
}
