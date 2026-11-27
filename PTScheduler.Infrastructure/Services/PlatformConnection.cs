namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Połączenie instancji trenera z Portalem platformy (zmienne ustawiane przy tworzeniu
/// kontenera). Instancja „zarządzana” dostaje od Portalu wspólne usługi — e-mail, SMS,
/// wideo — więc trener nie konfiguruje ich sam.
/// </summary>
public static class PlatformConnection
{
    public static string? PortalUrl => Environment.GetEnvironmentVariable("PORTAL_URL")?.TrimEnd('/');
    public static string? Slug => Environment.GetEnvironmentVariable("TENANT_SLUG");
    public static string? Secret => Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");

    public static bool IsManaged =>
        !string.IsNullOrEmpty(PortalUrl) && !string.IsNullOrEmpty(Slug) && !string.IsNullOrEmpty(Secret);

    /// <summary>Adres endpointu Portalu dla tej instancji, np. <c>/smtp</c>.</summary>
    public static string TenantApi(string suffix) =>
        $"{PortalUrl}/api/internal/tenants/{Uri.EscapeDataString(Slug ?? "")}{suffix}";

    public static HttpRequestMessage Request(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-Internal-Secret", Secret);
        return req;
    }
}
