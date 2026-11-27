namespace PTScheduler.Web.Services;

/// <summary>
/// Publiczny adres aplikacji poza żądaniem HTTP (usługi w tle, e-maile).
/// Portal ustawia TENANT_DOMAIN przy zakładaniu kontenera tenanta.
/// </summary>
public static class AppUrls
{
    public static string? PublicBase()
    {
        var domain = Environment.GetEnvironmentVariable("TENANT_DOMAIN")?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(domain)) return null;
        return domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? domain
            : $"https://{domain}";
    }

    /// <summary>Pełny adres ścieżki; bez skonfigurowanej domeny — sama ścieżka.</summary>
    public static string Absolute(string path) => (PublicBase() ?? "") + path;
}
