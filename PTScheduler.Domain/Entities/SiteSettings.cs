namespace PTScheduler.Domain.Entities;

// Singleton configuration row (Id is fixed to 1). Holds per-instance module
// toggles and the public Welcome/landing page content, editable by the Owner.
public class SiteSettings
{
    public int Id { get; set; } = 1;

    // Module feature flags — turn whole areas of the app on/off per instance.
    public bool WelcomeEnabled { get; set; } = true;
    public bool SchedulerEnabled { get; set; } = true;
    public bool AcademyEnabled { get; set; } = true;
    public bool ShopEnabled { get; set; } = false;

    // Public landing (Welcome) content.
    public string HeroHeadline { get; set; } = string.Empty;
    public string? HeroSubheadline { get; set; }
    public string? HeroImageUrl { get; set; }
    public string? HeroCtaLabel { get; set; }
    public string? HeroCtaUrl { get; set; }

    // Free-form marketing body, rendered as HTML (authored by the trusted Owner).
    public string? BodyHtml { get; set; }

    public string? ContactEmail { get; set; }

    // Payment gateway — PayU
    public bool PayUIsSandbox { get; set; } = true;
    public string? PayUPosId { get; set; }
    public string? PayUClientId { get; set; }
    public string? PayUClientSecret { get; set; }
    public string? PayUSecondKey { get; set; }
}
