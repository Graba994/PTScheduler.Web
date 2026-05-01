namespace PTScheduler.Domain.Entities;

public class AppBranding
{
    public int Id { get; set; } = 1;
    public string ThemeName { get; set; } = "ocean";
    public string CompanyName { get; set; } = "PTScheduler";
    public string? LogoPath { get; set; }
    public string? FaviconPath { get; set; }
}
