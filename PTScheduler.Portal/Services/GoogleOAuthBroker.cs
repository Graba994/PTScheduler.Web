using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Pośrednik OAuth Google dla instancji trenerów. Jeden klient OAuth platformy
/// (konfigurowany raz w Portalu), jeden adres powrotu — trener tylko klika
/// „Połącz z Google”. Refresh tokeny zostają tutaj (zaszyfrowane), a instancje
/// pobierają krótkotrwałe tokeny dostępu przez API wewnętrzne.
/// </summary>
public class GoogleOAuthBroker(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    ILogger<GoogleOAuthBroker> logger)
{
    public const string CallbackPath = "/oauth/google/callback";

    /// <summary>
    /// Kalendarz (wydarzenia na wszystkich kalendarzach — zapis wizyt, odczyt zajętości
    /// i tworzenie Meet) + adres e-mail do pokazania, które konto jest podłączone.
    /// </summary>
    private const string Scopes = "openid email https://www.googleapis.com/auth/calendar.events";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<int, (string Token, DateTime ExpiresUtc)> AccessCache = new();

    private IDataProtector TokenProtector => dataProtection.CreateProtector("google-calendar-refresh-token");
    private ITimeLimitedDataProtector StateProtector =>
        dataProtection.CreateProtector("google-calendar-oauth-state").ToTimeLimitedDataProtector();

    private sealed record State(string Slug, string UserKey, string ReturnUrl);

    public record Config(string ClientId, string ClientSecret, string RedirectUri);

    public async Task<Config?> GetConfigAsync()
    {
        var s = await settings.GetAllAsync(
            SiteSettingsService.Keys.PlatformGoogleClientId,
            SiteSettingsService.Keys.PlatformGoogleClientSecret,
            SiteSettingsService.Keys.PlatformGoogleRedirectUri);
        var id = s.GetValueOrDefault(SiteSettingsService.Keys.PlatformGoogleClientId);
        var secret = s.GetValueOrDefault(SiteSettingsService.Keys.PlatformGoogleClientSecret);
        var redirect = s.GetValueOrDefault(SiteSettingsService.Keys.PlatformGoogleRedirectUri);
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(redirect)
            ? null
            : new Config(id, secret, redirect);
    }

    /// <summary>
    /// Adres powrotu musi wskazywać na domenę tej instancji — inaczej ktoś mógłby
    /// użyć Portalu jako otwartego przekierowania.
    /// </summary>
    public static bool IsReturnUrlAllowed(Tenant tenant, string returnUrl)
    {
        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)) return false;
        var domain = tenant.Domain.Trim();
        if (Uri.TryCreate(domain, UriKind.Absolute, out var d)) domain = d.Host;
        domain = domain.TrimEnd('/');
        if (!string.Equals(uri.Host, domain, StringComparison.OrdinalIgnoreCase)) return false;
        return uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback;
    }

    public async Task<(string? Url, string? Error)> BuildAuthorizeUrlAsync(Tenant tenant, string userKey, string returnUrl)
    {
        var cfg = await GetConfigAsync();
        if (cfg is null) return (null, "Połączenie z Google nie jest jeszcze włączone na platformie.");
        if (string.IsNullOrWhiteSpace(userKey) || userKey.Length > 64) return (null, "Nieprawidłowy użytkownik.");
        if (!IsReturnUrlAllowed(tenant, returnUrl)) return (null, "Adres powrotu nie należy do tej instancji.");

        var state = StateProtector.Protect(JsonSerializer.Serialize(new State(tenant.Slug, userKey, returnUrl)), TimeSpan.FromMinutes(15));
        var url = "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={Uri.EscapeDataString(cfg.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(cfg.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(Scopes)}"
            + "&access_type=offline&prompt=consent&include_granted_scopes=true"
            + $"&state={Uri.EscapeDataString(state)}";
        return (url, null);
    }

    /// <summary>Obsługa powrotu z Google. Zwraca adres, na który odesłać przeglądarkę.</summary>
    public async Task<(string RedirectTo, bool Ok)> HandleCallbackAsync(string? code, string? stateRaw, string? error)
    {
        State? state;
        try { state = JsonSerializer.Deserialize<State>(StateProtector.Unprotect(stateRaw ?? "")); }
        catch { return ("/", false); } // wygasły albo podrobiony state — nie wiemy nawet, dokąd wrócić

        if (state is null) return ("/", false);
        string Back(string result) => state.ReturnUrl + (state.ReturnUrl.Contains('?') ? "&" : "?") + "google=" + result;

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return (Back("denied"), false);

        var cfg = await GetConfigAsync();
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == state.Slug);
        if (cfg is null || tenant is null) return (Back("error"), false);

        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["redirect_uri"] = cfg.RedirectUri,
            ["grant_type"] = "authorization_code"
        }));
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Google OAuth: wymiana kodu nie powiodła się dla {Slug}: {Status} {Body}", state.Slug, (int)resp.StatusCode, body);
            return (Back("error"), false);
        }
        var token = JsonSerializer.Deserialize<TokenResponse>(body, Json);
        if (string.IsNullOrEmpty(token?.RefreshToken)) return (Back("error"), false);
        if (token.Scope is not null && !token.Scope.Contains("calendar.events")) return (Back("scope"), false);

        var grant = await db.GoogleCalendarGrants.FirstOrDefaultAsync(g => g.TenantId == tenant.Id && g.UserKey == state.UserKey);
        if (grant is null)
        {
            grant = new GoogleCalendarGrant { TenantId = tenant.Id, UserKey = state.UserKey };
            db.GoogleCalendarGrants.Add(grant);
        }
        grant.RefreshToken = TokenProtector.Protect(token.RefreshToken);
        grant.GoogleEmail = EmailFromIdToken(token.IdToken);
        grant.CreatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        AccessCache.TryRemove(grant.Id, out _);
        return (Back("connected"), true);
    }

    public async Task<GoogleCalendarGrant?> GetGrantAsync(int tenantId, string userKey)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.GoogleCalendarGrants.AsNoTracking().FirstOrDefaultAsync(g => g.TenantId == tenantId && g.UserKey == userKey);
    }

    /// <summary>
    /// Token dostępu dla użytkownika instancji. <c>Revoked</c> = użytkownik cofnął
    /// dostęp w Google (zgoda usunięta — trzeba połączyć ponownie).
    /// </summary>
    public async Task<(string? AccessToken, int ExpiresIn, string? Email, bool Revoked)> GetAccessTokenAsync(int tenantId, string userKey)
    {
        var cfg = await GetConfigAsync();
        await using var db = dbFactory.CreateDbContext();
        var grant = await db.GoogleCalendarGrants.FirstOrDefaultAsync(g => g.TenantId == tenantId && g.UserKey == userKey);
        if (grant is null || cfg is null) return (null, 0, null, grant is null);

        if (AccessCache.TryGetValue(grant.Id, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(2))
            return (cached.Token, (int)(cached.ExpiresUtc - DateTime.UtcNow).TotalSeconds, grant.GoogleEmail, false);

        string refresh;
        try { refresh = TokenProtector.Unprotect(grant.RefreshToken); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google OAuth: nie da się odszyfrować tokenu (grant {Id}).", grant.Id);
            return (null, 0, grant.GoogleEmail, true);
        }

        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["refresh_token"] = refresh,
            ["grant_type"] = "refresh_token"
        }));
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var revoked = body.Contains("invalid_grant", StringComparison.Ordinal);
            logger.LogWarning("Google OAuth: odświeżenie tokenu nie powiodło się (grant {Id}): {Status}", grant.Id, (int)resp.StatusCode);
            if (revoked)
            {
                db.GoogleCalendarGrants.Remove(grant);
                await db.SaveChangesAsync();
            }
            return (null, 0, grant.GoogleEmail, revoked);
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(body, Json)!;
        var expires = DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn));
        AccessCache[grant.Id] = (token.AccessToken!, expires);
        grant.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (token.AccessToken, token.ExpiresIn, grant.GoogleEmail, false);
    }

    /// <summary>Odłącza konto: cofa zgodę w Google i usuwa token z Portalu.</summary>
    public async Task DisconnectAsync(int tenantId, string userKey)
    {
        await using var db = dbFactory.CreateDbContext();
        var grant = await db.GoogleCalendarGrants.FirstOrDefaultAsync(g => g.TenantId == tenantId && g.UserKey == userKey);
        if (grant is null) return;
        try
        {
            using var http = httpFactory.CreateClient();
            await http.PostAsync("https://oauth2.googleapis.com/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = TokenProtector.Unprotect(grant.RefreshToken) }));
        }
        catch (Exception ex) { logger.LogInformation(ex, "Google OAuth: cofnięcie zgody nie powiodło się (grant {Id}).", grant.Id); }
        AccessCache.TryRemove(grant.Id, out _);
        db.GoogleCalendarGrants.Remove(grant);
        await db.SaveChangesAsync();
    }

    /// <summary>Sprawdza klienta OAuth bez udziału użytkownika (Google odpowiada „invalid_grant” dla poprawnego klienta).</summary>
    public async Task<(bool Ok, string Message)> TestClientAsync(string clientId, string clientSecret)
    {
        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = "test",
            ["grant_type"] = "refresh_token"
        }));
        var body = await resp.Content.ReadAsStringAsync();
        if (body.Contains("invalid_grant", StringComparison.Ordinal)) return (true, "Klient OAuth poprawny.");
        if (body.Contains("invalid_client", StringComparison.Ordinal) || body.Contains("unauthorized_client", StringComparison.Ordinal))
            return (false, "Google nie rozpoznaje tego Client ID / Client Secret.");
        return (false, $"Nieoczekiwana odpowiedź Google ({(int)resp.StatusCode}).");
    }

    private static string? EmailFromIdToken(string? idToken)
    {
        // id_token przyszedł bezpośrednio z endpointu tokenów Google (TLS), więc
        // wystarczy odczytać ładunek — służy tylko do pokazania adresu trenerowi.
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 }) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }
}
