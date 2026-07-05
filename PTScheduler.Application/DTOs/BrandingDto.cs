namespace PTScheduler.Application.DTOs;

public class AppBrandingDto
{
    public string ThemeName { get; set; } = "ocean";
    public string ThemeMode { get; set; } = "light";
    public string CompanyName { get; set; } = "PTScheduler";
    public string? LogoPath { get; set; }
    public string? FaviconPath { get; set; }

    public string? PwaShortName { get; set; }
    public bool PwaBannerEnabled { get; set; } = true;
    public string? PwaBannerTitle { get; set; }
    public string? PwaBannerBody { get; set; }
    public string? PwaBannerButton { get; set; }
    public string? PwaIconPath { get; set; }
}

public class SaveBrandingDto
{
    public string ThemeName { get; set; } = "ocean";
    public string ThemeMode { get; set; } = "light";
    public string CompanyName { get; set; } = "PTScheduler";

    public string? PwaShortName { get; set; }
    public bool PwaBannerEnabled { get; set; } = true;
    public string? PwaBannerTitle { get; set; }
    public string? PwaBannerBody { get; set; }
    public string? PwaBannerButton { get; set; }
}
