namespace PTScheduler.Portal.Entities;

/// <summary>
/// Zgłoszenie błędu z aplikacji trenera (przycisk „Zgłoś błąd”): komentarz, opcjonalny zrzut ekranu
/// i kontekst strony (adres, przeglądarka, wersja aplikacji, ostatnie błędy w przeglądarce).
/// </summary>
public class BugReport
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Comment { get; set; } = "";
    /// <summary>Ścieżka w aplikacji trenera, np. /calendar?view=week.</summary>
    public string? PageUrl { get; set; }
    public string? PageTitle { get; set; }

    public string? AuthorEmail { get; set; }
    public string? AuthorName { get; set; }
    /// <summary>Rola w aplikacji: właściciel, trener, pracownik.</summary>
    public string? AuthorRole { get; set; }

    public string? UserAgent { get; set; }
    /// <summary>Okno przeglądarki, np. „390×844 @3x”.</summary>
    public string? Viewport { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>Ostatnie błędy JavaScript / konsoli z przeglądarki (każdy w osobnej linii).</summary>
    public string? ClientErrors { get; set; }

    /// <summary>Zrzut ekranu (JPEG/PNG). Po 90 dniach od zamknięcia zgłoszenia jest usuwany.</summary>
    public byte[]? Screenshot { get; set; }
    public string? ScreenshotType { get; set; }

    public BugReportStatus Status { get; set; } = BugReportStatus.New;
    public string? AdminNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
}

public enum BugReportStatus
{
    New,
    InProgress,
    Fixed,
    Closed
}
