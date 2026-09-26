namespace PTScheduler.Portal.Entities;

/// <summary>
/// Zgoda Google konkretnego użytkownika instancji trenera (kalendarz + Meet).
/// Refresh token zostaje w Portalu razem z sekretem klienta OAuth platformy —
/// instancja dostaje wyłącznie krótkotrwałe tokeny dostępu.
/// </summary>
public class GoogleCalendarGrant
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Id użytkownika w aplikacji trenera (AspNetUsers.Id).</summary>
    public string UserKey { get; set; } = string.Empty;
    public string? GoogleEmail { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
