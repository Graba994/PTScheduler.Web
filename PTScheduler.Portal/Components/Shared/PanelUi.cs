using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Components.Shared;

/// <summary>
/// Jedno miejsce na polskie nazwy, ikony i kolory (tony pp-tone-*) stanów w panelu —
/// żeby pulpit, lista trenerów i karta trenera mówiły tym samym językiem.
/// </summary>
public static class PanelUi
{
    public static string TenantStatusLabel(TenantStatus s) => s switch
    {
        TenantStatus.Active => "Aktywny",
        TenantStatus.Pending => "Oczekuje",
        TenantStatus.Provisioning => "Tworzenie…",
        TenantStatus.Suspended => "Zawieszony",
        TenantStatus.Destroyed => "Usunięty",
        _ => s.ToString()
    };

    public static string TenantStatusTone(TenantStatus s) => s switch
    {
        TenantStatus.Active => "ok",
        TenantStatus.Pending => "warn",
        TenantStatus.Provisioning => "info",
        TenantStatus.Suspended => "danger",
        _ => "muted"
    };

    public static string EventLabel(string type) => type switch
    {
        TenantEventTypes.Created => "Utworzony",
        TenantEventTypes.Provisioned => "Instancja uruchomiona",
        TenantEventTypes.Suspended => "Zawieszony",
        TenantEventTypes.Resumed => "Wznowiony",
        TenantEventTypes.Deleted => "Usunięty",
        TenantEventTypes.PlanChanged => "Zmiana planu",
        TenantEventTypes.AddonChanged => "Zmiana dodatków",
        TenantEventTypes.PaymentReceived => "Płatność otrzymana",
        TenantEventTypes.PaymentFailed => "Płatność nieudana",
        TenantEventTypes.TrialStarted => "Początek okresu próbnego",
        TenantEventTypes.TrialExpired => "Koniec okresu próbnego",
        TenantEventTypes.TrialWarning => "Okres próbny się kończy",
        TenantEventTypes.DomainChanged => "Zmiana domeny",
        TenantEventTypes.HealthDown => "Awaria",
        TenantEventTypes.HealthRecovered => "Znowu działa",
        TenantEventTypes.InactivityWarning => "Ostrzeżenie o nieaktywności",
        TenantEventTypes.InactivitySuspended => "Zawieszony za nieaktywność",
        TenantEventTypes.GraceExtended => "Przedłużony czas na płatność",
        TenantEventTypes.CleanupWarning => "Ostrzeżenie przed usunięciem",
        TenantEventTypes.CleanupDeleted => "Usunięty po czasie",
        TenantEventTypes.CreditAdded => "Doładowanie kredytów",
        TenantEventTypes.CreditDeducted => "Pobranie kredytów",
        TenantEventTypes.OfferChanged => "Zmiana oferty",
        TenantEventTypes.BillIssued => "Wystawiony rachunek",
        TenantEventTypes.BillPaid => "Opłacony rachunek",
        TenantEventTypes.ErrorSpike => "Fala błędów",
        TenantEventTypes.BackupVerified => "Kopia sprawdzona",
        TenantEventTypes.BackupVerifyFailed => "Kopia nie do odtworzenia",
        TenantEventTypes.BackupRestored => "Odtworzono z kopii",
        TenantEventTypes.Activated => "Aktywny trener",
        TenantEventTypes.ReferralReward => "Nagroda za polecenie",
        TenantEventTypes.OnboardingEmail => "E-mail powitalny",
        _ => type
    };

    public static string EventIcon(string type) => type switch
    {
        TenantEventTypes.Created => "bi-plus-circle",
        TenantEventTypes.Provisioned => "bi-rocket-takeoff",
        TenantEventTypes.Suspended or TenantEventTypes.InactivitySuspended => "bi-pause-circle",
        TenantEventTypes.Resumed => "bi-play-circle",
        TenantEventTypes.Deleted or TenantEventTypes.CleanupDeleted => "bi-trash",
        TenantEventTypes.PlanChanged => "bi-arrow-repeat",
        TenantEventTypes.AddonChanged => "bi-puzzle",
        TenantEventTypes.PaymentReceived => "bi-cash-stack",
        TenantEventTypes.PaymentFailed => "bi-exclamation-circle",
        TenantEventTypes.TrialStarted => "bi-clock",
        TenantEventTypes.TrialExpired => "bi-clock-history",
        TenantEventTypes.TrialWarning or TenantEventTypes.InactivityWarning or TenantEventTypes.CleanupWarning => "bi-exclamation-triangle",
        TenantEventTypes.DomainChanged => "bi-globe",
        TenantEventTypes.HealthDown => "bi-heart-pulse",
        TenantEventTypes.HealthRecovered => "bi-heart",
        TenantEventTypes.GraceExtended => "bi-calendar-plus",
        TenantEventTypes.CreditAdded => "bi-plus-square",
        TenantEventTypes.CreditDeducted => "bi-dash-square",
        TenantEventTypes.OfferChanged => "bi-file-earmark-text",
        TenantEventTypes.BillIssued => "bi-receipt",
        TenantEventTypes.BillPaid => "bi-receipt-cutoff",
        TenantEventTypes.ErrorSpike => "bi-bug",
        TenantEventTypes.BackupVerified => "bi-shield-check",
        TenantEventTypes.BackupVerifyFailed => "bi-shield-x",
        TenantEventTypes.BackupRestored => "bi-arrow-counterclockwise",
        TenantEventTypes.Activated => "bi-lightning-charge-fill",
        TenantEventTypes.ReferralReward => "bi-gift",
        TenantEventTypes.OnboardingEmail => "bi-envelope-heart",
        _ => "bi-circle"
    };

    public static string EventTone(string type) => type switch
    {
        TenantEventTypes.Created or TenantEventTypes.Provisioned or TenantEventTypes.Resumed
            or TenantEventTypes.PaymentReceived or TenantEventTypes.HealthRecovered or TenantEventTypes.CreditAdded or TenantEventTypes.BillPaid or TenantEventTypes.BackupVerified or TenantEventTypes.BackupRestored or TenantEventTypes.Activated or TenantEventTypes.ReferralReward => "ok",
        TenantEventTypes.Suspended or TenantEventTypes.Deleted or TenantEventTypes.PaymentFailed or TenantEventTypes.TrialExpired
            or TenantEventTypes.HealthDown or TenantEventTypes.InactivitySuspended or TenantEventTypes.CleanupDeleted
            or TenantEventTypes.ErrorSpike or TenantEventTypes.BackupVerifyFailed => "danger",
        TenantEventTypes.TrialWarning or TenantEventTypes.InactivityWarning or TenantEventTypes.CleanupWarning => "warn",
        TenantEventTypes.PlanChanged or TenantEventTypes.AddonChanged or TenantEventTypes.DomainChanged
            or TenantEventTypes.TrialStarted or TenantEventTypes.GraceExtended or TenantEventTypes.BillIssued => "info",
        _ => "muted"
    };

    public static string PaymentTone(PaymentRecordStatus s) => s switch
    {
        PaymentRecordStatus.Paid => "ok",
        PaymentRecordStatus.Failed => "danger",
        PaymentRecordStatus.Refunded => "warn",
        _ => "muted"
    };

    public static string PaymentLabel(PaymentRecordStatus s) => s switch
    {
        PaymentRecordStatus.Paid => "opłacona",
        PaymentRecordStatus.Failed => "nieudana",
        PaymentRecordStatus.Refunded => "zwrócona",
        PaymentRecordStatus.Pending => "w toku",
        _ => s.ToString()
    };

    /// <summary>Polska odmiana: 1 trener, 2 trenerów… → Plural(n, "instancja", "instancje", "instancji").</summary>
    public static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;

    public static string MeterTone(int pct) => pct > 80 ? "pp-tone-danger" : pct > 60 ? "pp-tone-warn" : "pp-tone-ok";
}
