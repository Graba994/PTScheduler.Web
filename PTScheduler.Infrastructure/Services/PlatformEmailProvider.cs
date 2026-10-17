using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Poczta przez Portal platformy (instancja zarządzana). Portal wysyła w imieniu
/// studia — instancja nie zna hasła do serwera SMTP, a każdy trener ma własny
/// dzienny limit, więc nie wpływa na wysyłkę pozostałych.
/// </summary>
public class PlatformEmailProvider(IHttpClientFactory httpFactory, ILogger<PlatformEmailProvider> logger)
{
    public sealed record MailStatus(bool Enabled, int DailyLimit, int SentToday);

    private MailStatus? _cached;
    private DateTime _cachedUntil;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Czy Portal udostępnia pocztę (pamiętane 5 minut); null poza instancją zarządzaną.</summary>
    public async Task<MailStatus?> GetStatusAsync(bool refresh = false)
    {
        if (!PlatformConnection.IsManaged) return null;
        if (!refresh && DateTime.UtcNow < _cachedUntil) return _cached;

        await _lock.WaitAsync();
        try
        {
            if (!refresh && DateTime.UtcNow < _cachedUntil) return _cached;
            try
            {
                using var http = httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                using var req = PlatformConnection.Request(HttpMethod.Get, PlatformConnection.TenantApi("/mail/status"));
                using var resp = await http.SendAsync(req);
                _cached = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<MailStatus>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
                if (_cached is { Enabled: false }) _cached = null;
                _cachedUntil = DateTime.UtcNow.AddMinutes(5);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Nie pobrano stanu poczty platformy — spróbuję za minutę.");
                _cachedUntil = DateTime.UtcNow.AddMinutes(1);
            }
            return _cached;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Wysyła przez Portal. Rzuca wyjątek z czytelnym opisem, gdy się nie uda.</summary>
    public async Task SendAsync(string to, string toName, string subject, string html, string fromName, string? replyTo)
    {
        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        using var req = PlatformConnection.Request(HttpMethod.Post, PlatformConnection.TenantApi("/mail"));
        req.Content = JsonContent.Create(new { to, toName, subject, html, fromName, replyTo });
        using var resp = await http.SendAsync(req);
        if (resp.IsSuccessStatusCode) return;

        string? error = null;
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
        }
        catch { /* odpowiedź bez JSON */ }
        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable) _cachedUntil = DateTime.MinValue; // poczta wyłączona — odśwież stan
        throw new InvalidOperationException(error ?? $"Portal odrzucił wiadomość ({(int)resp.StatusCode}).");
    }
}
