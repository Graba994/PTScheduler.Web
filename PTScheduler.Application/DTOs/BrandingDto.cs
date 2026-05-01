namespace PTScheduler.Application.DTOs;

public class AppBrandingDto
{
    public string ThemeName { get; set; } = "ocean";
    public string CompanyName { get; set; } = "PTScheduler";
    public string? LogoPath { get; set; }
    public string? FaviconPath { get; set; }
}

public class SaveBrandingDto
{
    public string ThemeName { get; set; } = "ocean";
    public string CompanyName { get; set; } = "PTScheduler";
}
