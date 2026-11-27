namespace PTScheduler.Portal.Entities;

/// <summary>
/// Akceptacja dokumentu prawnego (regulamin, umowa powierzenia, polityka prywatności) — kto, kiedy, którą wersję
/// i skąd. Dowód zawarcia umowy na odległość i powierzenia danych (art. 28 RODO).
/// </summary>
public class LegalAcceptance
{
    public long Id { get; set; }
    public int? TenantId { get; set; }
    public string Email { get; set; } = "";
    public string DocKey { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>„kreator”, „aplikacja” (akceptacja w aplikacji trenera), „panel”.</summary>
    public string Source { get; set; } = "kreator";
}
