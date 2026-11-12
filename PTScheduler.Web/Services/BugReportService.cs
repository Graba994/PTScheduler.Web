using System.Net.Http.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Zgłoś błąd” w aplikacji trenera: komentarz, zrzut ekranu i kontekst strony idą do Portalu
/// (POST /api/internal/tenants/{slug}/bug-reports, sekret instancji). Przycisk widzi zespół studia
/// (właściciel, trenerzy, pracownicy) — nie klienci i nie goście publicznego demo.
/// </summary>
public sealed class BugReportService(IHttpClientFactory httpFactory, PublicDemoService demo, ILogger<BugReportService> logger)
{
    public const int MaxComment = 2000;
    public const int MaxScreenshotBytes = 3 * 1024 * 1024;

    /// <summary>Kontekst z przeglądarki (ptBug.context w bug-report.js).</summary>
    public sealed class PageContext
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? Viewport { get; set; }
        public string? UserAgent { get; set; }
        public string? Errors { get; set; }
    }

    public sealed record Author(string? Email, string? Name, string? Role);

    private static (string Url, string Slug, string Secret)? Portal()
    {
        var url = Environment.GetEnvironmentVariable("PORTAL_URL");
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG");
        var secret = Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");
        return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(secret)
            ? null : (url.TrimEnd('/'), slug, secret);
    }

    /// <summary>Instancja połączona z Portalem i nie jest publicznym demo.</summary>
    public bool IsAvailable => Portal() is not null && !demo.IsOn;

    /// <summary>Wersja aplikacji z obrazu Dockera (BUILD_COMMIT / BUILD_TIME), np. „1e9f603 · 2026-10-23 08:12”.</summary>
    public static string? AppVersion()
    {
        var commit = Environment.GetEnvironmentVariable("BUILD_COMMIT");
        var time = Environment.GetEnvironmentVariable("BUILD_TIME");
        if (string.IsNullOrWhiteSpace(commit) || commit == "unknown") return null;
        var shortCommit = commit.Length > 7 ? commit[..7] : commit;
        return string.IsNullOrWhiteSpace(time) || time == "unknown" ? shortCommit : $"{shortCommit} · {time}";
    }

    public async Task<(bool Ok, string? Error)> SendAsync(string? comment, byte[]? screenshot, PageContext? page, Author author)
    {
        comment = comment?.Trim();
        if (string.IsNullOrEmpty(comment)) return (false, "Napisz w kilku słowach, co się stało.");
        if (comment.Length > MaxComment) return (false, $"Opis może mieć najwyżej {MaxComment} znaków.");
        if (screenshot is { Length: > MaxScreenshotBytes }) screenshot = null;
        if (Portal() is not { } portal) return (false, "Ta instalacja nie jest połączona z Hantly — zgłoszenia nie da się wysłać.");
        if (demo.IsOn) return (false, "W wersji demo zgłoszenia są wyłączone.");

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{portal.Url}/api/internal/tenants/{Uri.EscapeDataString(portal.Slug)}/bug-reports")
            {
                Content = JsonContent.Create(new
                {
                    comment,
                    pageUrl = page?.Url,
                    pageTitle = page?.Title,
                    authorEmail = author.Email,
                    authorName = author.Name,
                    authorRole = author.Role,
                    userAgent = page?.UserAgent,
                    viewport = page?.Viewport,
                    appVersion = AppVersion(),
                    clientErrors = page?.Errors,
                    screenshot = screenshot is null ? null : Convert.ToBase64String(screenshot),
                    screenshotType = screenshot is null ? null : "image/jpeg"
                })
            };
            req.Headers.Add("X-Internal-Secret", portal.Secret);
            using var resp = await http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return (true, null);

            string? reason = null;
            if ((int)resp.StatusCode == 400)
            {
                try { reason = (await resp.Content.ReadFromJsonAsync<ErrorBody>())?.Error; }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException) { }
            }
            logger.LogWarning("Portal odrzucił zgłoszenie błędu: {Status}.", (int)resp.StatusCode);
            return (false, reason ?? "Nie udało się wysłać zgłoszenia. Spróbuj za chwilę.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Zgłoszenie błędu nie dotarło do Portalu.");
            return (false, "Nie udało się połączyć z Hantly. Spróbuj za chwilę.");
        }
    }

    private sealed record ErrorBody(string? Error);
}
