namespace PTScheduler.Domain.Entities;

public class AppBranding
{
    public int Id { get; set; } = 1;
    public string ThemeName { get; set; } = "ocean";
    public string ThemeMode { get; set; } = "light"; // "light" | "dark" | "system"
    public string CompanyName { get; set; } = "PTScheduler";
    public string? LogoPath { get; set; }
    public string? FaviconPath { get; set; }

    // PWA install banner
    public string? PwaShortName { get; set; }
    public bool PwaBannerEnabled { get; set; } = true;
    public string? PwaBannerTitle { get; set; }
    public string? PwaBannerBody { get; set; }
    public string? PwaBannerButton { get; set; }
    public string? PwaIconPath { get; set; }

    public bool SetupCompleted { get; set; }
    public string? SetupMode { get; set; }
    public DateTime? SetupCompletedAt { get; set; }
}
