namespace PTScheduler.Application.DTOs;

public class SiteSettingsDto
{
    public bool WelcomeEnabled { get; set; } = true;
    public bool SchedulerEnabled { get; set; } = true;
    public bool AcademyEnabled { get; set; } = true;
    public bool ShopEnabled { get; set; }

    public string HeroHeadline { get; set; } = string.Empty;
    public string? HeroSubheadline { get; set; }
    public string? HeroImageUrl { get; set; }
    public string? HeroCtaLabel { get; set; }
    public string? HeroCtaUrl { get; set; }
    public string? BodyHtml { get; set; }
    public string? ContactEmail { get; set; }

    // PayU
    public bool PayUIsSandbox { get; set; } = true;
    public string? PayUPosId { get; set; }
    public string? PayUClientId { get; set; }
    public string? PayUClientSecret { get; set; }
    public string? PayUSecondKey { get; set; }
}
