using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace PTScheduler.Infrastructure.Services;

/// <summary>Wspólny serwer SMTP platformy (pobierany z Portalu, pamiętany 10 minut).</summary>
public class PlatformEmailProvider(IHttpClientFactory httpFactory, ILogger<PlatformEmailProvider> logger)
{
    public sealed record PlatformSmtp(string Host, int Port, bool Ssl, string? User, string? Password, string FromAddress);

    private PlatformSmtp? _cached;
    private DateTime _cachedUntil;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<PlatformSmtp?> GetAsync()
    {
        if (!PlatformConnection.IsManaged) return null;
        if (DateTime.UtcNow < _cachedUntil) return _cached;

        await _lock.WaitAsync();
        try
        {
            if (DateTime.UtcNow < _cachedUntil) return _cached;
            try
            {
                using var http = httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                using var req = PlatformConnection.Request(HttpMethod.Get, PlatformConnection.TenantApi("/smtp"));
                using var resp = await http.SendAsync(req);
                _cached = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<SmtpResponse>() is { Enabled: true } r && !string.IsNullOrWhiteSpace(r.Host)
                    ? new PlatformSmtp(r.Host!, r.Port, r.Ssl, r.User, r.Password, r.FromAddress ?? r.User ?? "")
                    : null : null;
                _cachedUntil = DateTime.UtcNow.AddMinutes(10);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Nie pobrano ustawień SMTP platformy — spróbuję za minutę.");
                _cachedUntil = DateTime.UtcNow.AddMinutes(1);
            }
            return _cached;
        }
        finally { _lock.Release(); }
    }

    private sealed class SmtpResponse
    {
        public bool Enabled { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public bool Ssl { get; set; } = true;
        public string? User { get; set; }
        public string? Password { get; set; }
        public string? FromAddress { get; set; }
    }
}
