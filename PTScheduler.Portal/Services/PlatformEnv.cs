using System.Text;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Odtwarza plik <c>.env.prod</c> z konfiguracji działających kontenerów platformy (Portal, baza Portalu, Guardian).
/// Instalacja przez Portainera albo Unraid nie ma pliku na dysku — bez tego nowy serwer nie znałby
/// hasła bazy Portalu ani sekretu Guardiana.
/// </summary>
public static class PlatformEnv
{
    /// <summary>(kontener, zmienna w kontenerze, zmienna w docker-compose.prod.yml)</summary>
    private static readonly (string Container, string ContainerVar, string ComposeVar)[] Map =
    [
        ("db", "POSTGRES_DB", "PORTAL_DB_NAME"),
        ("db", "POSTGRES_USER", "PORTAL_DB_USER"),
        ("db", "POSTGRES_PASSWORD", "PORTAL_DB_PASSWORD"),
        ("portal", "Portal__TenantInternalSecret", "TENANT_INTERNAL_SECRET"),
        ("portal", "Portal__ForwardHost", "FORWARD_HOST"),
        ("portal", "Portal__PublicUrl", "PORTAL_PUBLIC_URL"),
        ("portal", "Portal__ContactEmail", "CONTACT_EMAIL"),
        ("portal", "Portal__SiteName", "SITE_NAME"),
        ("portal", "Portal__NpmUrl", "NPM_URL"),
        ("portal", "Portal__NpmUser", "NPM_USER"),
        ("portal", "Portal__NpmPassword", "NPM_PASSWORD"),
        ("portal", "Stripe__SecretKey", "STRIPE_SECRET_KEY"),
        ("portal", "Stripe__WebhookSecret", "STRIPE_WEBHOOK_SECRET"),
        ("portal", "Stripe__PublishableKey", "STRIPE_PUBLISHABLE_KEY"),
        ("portal", "Email__SmtpHost", "SMTP_HOST"),
        ("portal", "Email__SmtpPort", "SMTP_PORT"),
        ("portal", "Email__SmtpUser", "SMTP_USER"),
        ("portal", "Email__SmtpPassword", "SMTP_PASSWORD"),
        ("portal", "Email__FromAddress", "SMTP_FROM"),
        ("guardian", "GUARDIAN_SECRET", "GUARDIAN_SECRET"),
        ("guardian", "GUARDIAN_BRANCH", "BUILD_BRANCH"),
        ("guardian", "GUARDIAN_TENANT_HOST", "GUARDIAN_TENANT_HOST"),
    ];

    public static async Task<string> BuildAsync(DockerService docker, IConfiguration config)
    {
        var names = new Dictionary<string, string>
        {
            ["db"] = config["Portal:DbContainerName"] ?? "ptportal-db",
            ["portal"] = config["Portal:ContainerName"] ?? "ptportal",
            ["guardian"] = config["Portal:GuardianContainerName"] ?? "ptguardian",
        };
        var env = new Dictionary<string, Dictionary<string, string>>();
        var ports = new Dictionary<string, string>();
        foreach (var (key, name) in names)
        {
            var info = await docker.InspectAsync(name);
            env[key] = (info?.Config?.Env ?? [])
                .Select(e => e.Split('=', 2)).Where(p => p.Length == 2)
                .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
            var binding = info?.HostConfig?.PortBindings?.Values.FirstOrDefault()?.FirstOrDefault()?.HostPort;
            if (!string.IsNullOrEmpty(binding)) ports[key] = binding;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Konfiguracja platformy odczytana z działających kontenerów przez Portal.");
        sb.AppendLine($"# Utworzono {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Użyj jako .env.prod na nowym serwerze.");
        foreach (var (container, from, to) in Map)
            if (env.GetValueOrDefault(container)?.GetValueOrDefault(from) is { Length: > 0 } value)
                sb.AppendLine($"{to}={Escape(value)}");
        if (ports.TryGetValue("portal", out var portalPort)) sb.AppendLine($"PORTAL_PORT={portalPort}");
        if (ports.TryGetValue("guardian", out var guardianPort)) sb.AppendLine($"GUARDIAN_PORT={guardianPort}");
        return sb.ToString();
    }

    // Pojedyncze cudzysłowy = wartość dosłowna (bez podstawiania $ przez docker compose).
    private static string Escape(string v) =>
        v.IndexOfAny([' ', '#', '"', '\'', '$', '\\']) < 0 ? v
        : !v.Contains('\'') ? "'" + v + "'"
        : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
