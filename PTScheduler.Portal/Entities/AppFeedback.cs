namespace PTScheduler.Portal.Entities;

/// <summary>Ocena aplikacji wystawiona przez trenera z instancji tenanta.</summary>
public class AppFeedback
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int Rating { get; set; }
    public string? Text { get; set; }
    /// <summary>Kto wysłał (e-mail konta w aplikacji trenera).</summary>
    public string? AuthorEmail { get; set; }
    /// <summary>Podany tylko, gdy trener zgodził się na kontakt.</summary>
    public string? ContactEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
