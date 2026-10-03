using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PTScheduler.Infrastructure.Services.Google;

public sealed record PortalGoogleStatus(bool Available, bool Connected, string? Email);

/// <summary>
/// Dostęp do Google przez Portal platformy (instancja zarządzana). Portal trzyma
/// klienta OAuth i refresh tokeny; tu pobieramy tylko krótkie tokeny dostępu.
/// </summary>
public interface IGoogleTokenBroker
{
    bool IsManaged { get; }
    Task<PortalGoogleStatus?> GetStatusAsync(string userId, CancellationToken ct = default);
    Task<(string? Url, string? Error)> AuthorizeAsync(string userId, string returnUrl, CancellationToken ct = default);
    /// <summary><c>Revoked</c> = Portal nie ma już zgody tego użytkownika (trzeba połączyć ponownie).</summary>
    Task<(string? AccessToken, bool Revoked)> GetAccessTokenAsync(string userId, CancellationToken ct = default);
    Task DisconnectAsync(string userId, CancellationToken ct = default);
    /// <summary>Zapomina token z pamięci (np. gdy Google odrzucił go jako nieważny).</summary>
    void Invalidate(string userId);
}

public sealed class GoogleTokenBroker(IHttpClientFactory httpFactory, ILogger<GoogleTokenBroker> logger) : IGoogleTokenBroker
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresUtc)> Cache = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsManaged => PlatformConnection.IsManaged;

    public async Task<PortalGoogleStatus?> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        if (!IsManaged) return null;
        try
        {
            using var http = Client();
            using var req = PlatformConnection.Request(HttpMethod.Get, PlatformConnection.TenantApi($"/google/status?userKey={Uri.EscapeDataString(userId)}"));
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<PortalGoogleStatus>(Json, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Google: Portal nie odpowiada (status).");
            return null;
        }
    }

    public async Task<(string? Url, string? Error)> AuthorizeAsync(string userId, string returnUrl, CancellationToken ct = default)
    {
        if (!IsManaged) return (null, "Ta instalacja nie jest połączona z platformą.");
        try
        {
            using var http = Client();
            using var req = PlatformConnection.Request(HttpMethod.Post, PlatformConnection.TenantApi("/google/authorize"));
            req.Content = JsonContent.Create(new { userKey = userId, returnUrl });
            using var resp = await http.SendAsync(req, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (resp.IsSuccessStatusCode && doc.RootElement.TryGetProperty("url", out var url)) return (url.GetString(), null);
            return (null, doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "Nie udało się rozpocząć łączenia z Google.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Google: Portal nie odpowiada (authorize).");
            return (null, "Platforma chwilowo nie odpowiada. Spróbuj za chwilę.");
        }
    }

    public async Task<(string? AccessToken, bool Revoked)> GetAccessTokenAsync(string userId, CancellationToken ct = default)
    {
        if (!IsManaged) return (null, false);
        if (Cache.TryGetValue(userId, out var c) && c.ExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return (c.Token, false);
        try
        {
            using var http = Client();
            using var req = PlatformConnection.Request(HttpMethod.Post, PlatformConnection.TenantApi("/google/token"));
            req.Content = JsonContent.Create(new { userKey = userId });
            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) { Cache.TryRemove(userId, out _); return (null, true); }
            if (!resp.IsSuccessStatusCode) return (null, false);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var token = doc.RootElement.GetProperty("accessToken").GetString();
            var expiresIn = doc.RootElement.TryGetProperty("expiresIn", out var e) ? e.GetInt32() : 300;
            if (token is null) return (null, false);
            Cache[userId] = (token, DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn)));
            return (token, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Google: Portal nie odpowiada (token).");
            return (null, false);
        }
    }

    public void Invalidate(string userId) => Cache.TryRemove(userId, out _);

    public async Task DisconnectAsync(string userId, CancellationToken ct = default)
    {
        Cache.TryRemove(userId, out _);
        if (!IsManaged) return;
        try
        {
            using var http = Client();
            using var req = PlatformConnection.Request(HttpMethod.Post, PlatformConnection.TenantApi("/google/disconnect"));
            req.Content = JsonContent.Create(new { userKey = userId });
            using var _ = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Google: Portal nie odpowiada (disconnect).");
        }
    }

    private HttpClient Client()
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        return http;
    }
}
