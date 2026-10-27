using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Garmin Connect (program partnerski, plan Starter): Hantly jest jednym partnerem dla wszystkich studiów.
/// Logowanie klienta do Garmina (OAuth2 PKCE) prowadzi Portal; dane Garmin wysyła sam (push) na jeden adres
/// z kluczem — zapisujemy je od razu, a w tle przekazujemy do aplikacji właściwego studia (z ponawianiem).
/// Plan Starter: tylko push, bez historii sprzed połączenia, do 1000 połączonych osób.
/// </summary>
public sealed class GarminService(
    SiteSettingsService settings,
    IDbContextFactory<PortalDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    IConfiguration config,
    TenantService tenants,
    ILogger<GarminService> logger)
{
    public const string AuthorizeUrl = "https://connect.garmin.com/oauth2Confirm";
    public const string TokenUrl = "https://diauth.garmin.com/di-oauth2-service/oauth/token";
    public const string UserIdUrl = "https://apis.garmin.com/wellness-api/rest/user/id";
    public const string RegistrationUrl = "https://apis.garmin.com/wellness-api/rest/user/registration";
    /// <summary>Limit połączonych osób w planie Starter.</summary>
    public const int StarterLimit = 1000;
    /// <summary>Rodzaje danych przekazywane do studiów (reszta: odłączenia i zmiany uprawnień — obsługujemy tutaj).</summary>
    public static readonly string[] ForwardKinds = ["dailies", "sleeps", "hrv", "activities"];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private IDataProtector Secrets => dataProtection.CreateProtector("garmin-secrets");

    public sealed record Config(string ClientId, bool HasSecret, string PushKey)
    {
        public bool Enabled => ClientId.Length > 0 && HasSecret;
    }

    public async Task<Config> ConfigAsync()
    {
        var s = await settings.GetAllAsync(K.GarminClientId, K.GarminClientSecret, K.GarminPushKey);
        var key = s[K.GarminPushKey];
        // Klucz w adresie odbioru danych powstaje od razu — adres można wkleić do Garmina jeszcze przed kluczami aplikacji.
        if (string.IsNullOrWhiteSpace(key)) await settings.SetAsync(K.GarminPushKey, key = NewKey(24));
        return new(s[K.GarminClientId].Trim(), !string.IsNullOrWhiteSpace(s[K.GarminClientSecret]), key);
    }

    public async Task SaveConfigAsync(string clientId, string? secret)
    {
        var values = new Dictionary<string, string> { [K.GarminClientId] = clientId.Trim() };
        if (!string.IsNullOrWhiteSpace(secret)) values[K.GarminClientSecret] = Secrets.Protect(secret.Trim());
        if (string.IsNullOrWhiteSpace(await settings.GetAsync(K.GarminPushKey))) values[K.GarminPushKey] = NewKey(24);
        await settings.SetManyAsync(values);
    }

    public string PortalUrl => PortalUrls.Public(config);
    public string RedirectUri => $"{PortalUrl}/garmin/callback";
    public async Task<string> PushUrlAsync() => $"{PortalUrl}/api/garmin/push/{(await ConfigAsync()).PushKey}";

    private async Task<(string Id, string Secret)?> CredentialsAsync()
    {
        var s = await settings.GetAllAsync(K.GarminClientId, K.GarminClientSecret);
        if (string.IsNullOrWhiteSpace(s[K.GarminClientId]) || string.IsNullOrWhiteSpace(s[K.GarminClientSecret])) return null;
        try { return (s[K.GarminClientId].Trim(), Secrets.Unprotect(s[K.GarminClientSecret])); }
        catch (CryptographicException) { return null; }
    }

    public async Task<int> ActiveConnectionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.GarminConnections.CountAsync(c => c.DisconnectedAt == null && !c.IsTest);
    }

    // ── Logowanie (OAuth2 PKCE) ──

    /// <summary>Adres logowania do Garmina dla klienta studia. <paramref name="returnPath"/> — ścieżka w aplikacji studia.</summary>
    public async Task<(string? Url, string? Error)> StartAsync(Tenant tenant, int clientId, string? returnPath)
    {
        var cfg = await ConfigAsync();
        if (!cfg.Enabled) return (null, "Garmin nie jest jeszcze włączony na platformie.");
        if (await ActiveConnectionsAsync() >= StarterLimit) return (null, "Osiągnęliśmy limit połączeń z Garminem — napisz do nas, zwiększymy go.");
        var verifier = NewKey(64);
        var state = NewKey(32);
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.GarminAuthRequests.Where(r => r.CreatedAt < DateTime.UtcNow.AddHours(-1)).ExecuteDeleteAsync();
        db.GarminAuthRequests.Add(new GarminAuthRequest
        {
            State = state, CodeVerifier = verifier, TenantId = tenant.Id, TenantClientId = clientId, ReturnPath = SafePath(returnPath)
        });
        await db.SaveChangesAsync();
        var url = $"{AuthorizeUrl}?response_type=code&client_id={Uri.EscapeDataString(cfg.ClientId)}"
                  + $"&code_challenge={Challenge(verifier)}&code_challenge_method=S256"
                  + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state={state}";
        return (url, null);
    }

    /// <summary>Powrót z Garmina: wymiana kodu na tokeny, identyfikator użytkownika, zapis i powiadomienie studia.</summary>
    public async Task<string> CallbackAsync(string? code, string? state, string? error)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var req = string.IsNullOrEmpty(state) ? null : await db.GarminAuthRequests.FirstOrDefaultAsync(r => r.State == state);
        if (req is null || req.CreatedAt < DateTime.UtcNow.AddMinutes(-15)) return $"{PortalUrl}/?garmin=wygaslo";
        db.GarminAuthRequests.Remove(req);
        await db.SaveChangesAsync();

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TenantId);
        if (tenant is null) return PortalUrl;
        var back = (await tenants.PublicUrlAsync(tenant)).TrimEnd('/') + req.ReturnPath;
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return Append(back, "garmin=anulowano");

        try
        {
            var creds = await CredentialsAsync() ?? throw new InvalidOperationException("Brak kluczy Garmina.");
            var tokens = await TokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = creds.Id, ["client_secret"] = creds.Secret,
                ["code"] = code, ["code_verifier"] = req.CodeVerifier, ["redirect_uri"] = RedirectUri
            });
            var userId = await UserIdAsync(tokens.Access);

            // Jedno konto Garmin = jeden klient: ponowne połączenie (także w innym studiu) przenosi je.
            var conn = await db.GarminConnections.FirstOrDefaultAsync(c => c.GarminUserId == userId);
            var previous = conn is { DisconnectedAt: null } && (conn.TenantId != req.TenantId || conn.TenantClientId != req.TenantClientId)
                ? (conn.TenantId, conn.TenantClientId) : ((int, int)?)null;
            if (conn is null) db.GarminConnections.Add(conn = new GarminConnection { GarminUserId = userId });
            conn.TenantId = req.TenantId;
            conn.TenantClientId = req.TenantClientId;
            conn.IsTest = false;
            Store(conn, tokens);
            conn.ConnectedAt = DateTime.UtcNow;
            conn.DisconnectedAt = null;
            await db.SaveChangesAsync();

            if (previous is { } p && await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == p.Item1) is { } prevTenant)
                await NotifyLinkAsync(prevTenant, p.Item2, false);
            await NotifyLinkAsync(tenant, req.TenantClientId, true);
            return Append(back, "garmin=ok");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Połączenie Garmina dla studia {Slug} (klient {ClientId}) nie powiodło się.", tenant.Slug, req.TenantClientId);
            return Append(back, "garmin=blad");
        }
    }

    /// <summary>Odłączenie z aplikacji studia: wyrejestrowanie u Garmina i usunięcie tokenów.</summary>
    public async Task<string?> DisconnectAsync(Tenant tenant, int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var conns = await db.GarminConnections.Where(c => c.TenantId == tenant.Id && c.TenantClientId == clientId && c.DisconnectedAt == null).ToListAsync();
        string? error = null;
        foreach (var c in conns)
        {
            if (!c.IsTest)
            {
                try
                {
                    var token = await AccessTokenAsync(c);
                    if (token is not null)
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Delete, RegistrationUrl);
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        using var resp = await Http.SendAsync(req);
                        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                            error = $"Garmin odpowiedział {(int)resp.StatusCode}";
                    }
                }
                catch (Exception ex) { logger.LogWarning(ex, "Wyrejestrowanie Garmina {User} nie powiodło się.", c.GarminUserId); error = "Garmin nie odpowiedział"; }
            }
            c.DisconnectedAt = DateTime.UtcNow;
            c.AccessTokenProtected = c.RefreshTokenProtected = null;
        }
        await db.SaveChangesAsync();
        return error;
    }

    private sealed record Tokens(string Access, DateTime AccessExpires, string? Refresh);

    private static async Task<Tokens> TokenAsync(Dictionary<string, string> form)
    {
        using var resp = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Garmin token: {(int)resp.StatusCode} {text[..Math.Min(200, text.Length)]}");
        using var doc = JsonDocument.Parse(text);
        var r = doc.RootElement;
        var expires = r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var sec) ? sec : 3600;
        return new(r.GetProperty("access_token").GetString()!, DateTime.UtcNow.AddSeconds(expires - 60),
            r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null);
    }

    private static async Task<string> UserIdAsync(string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UserIdUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("userId").GetString() ?? throw new InvalidOperationException("Garmin nie zwrócił userId.");
    }

    private void Store(GarminConnection c, Tokens t)
    {
        c.AccessTokenProtected = Secrets.Protect(t.Access);
        c.AccessTokenExpiresAt = t.AccessExpires;
        if (t.Refresh is not null) c.RefreshTokenProtected = Secrets.Protect(t.Refresh);
    }

    /// <summary>Ważny token dostępu (odświeżany, gdy wygasł). Null — brak tokenów albo odświeżenie się nie udało.</summary>
    private async Task<string?> AccessTokenAsync(GarminConnection c)
    {
        try
        {
            if (c.AccessTokenProtected is not null && c.AccessTokenExpiresAt > DateTime.UtcNow) return Secrets.Unprotect(c.AccessTokenProtected);
            if (c.RefreshTokenProtected is null || await CredentialsAsync() is not { } creds) return null;
            var t = await TokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["client_id"] = creds.Id, ["client_secret"] = creds.Secret,
                ["refresh_token"] = Secrets.Unprotect(c.RefreshTokenProtected)
            });
            Store(c, t);
            return t.Access;
        }
        catch (Exception ex) { logger.LogWarning(ex, "Token Garmina {User} niedostępny.", c.GarminUserId); return null; }
    }

    // ── Dane z Garmina (push) ──

    /// <summary>Zapis paczki od Garmina. False — zły klucz w adresie (nie przyjmujemy).</summary>
    public async Task<bool> ReceiveAsync(string key, string payload)
    {
        var cfg = await ConfigAsync();
        if (cfg.PushKey.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(cfg.PushKey)))
            return false;
        await EnqueueAsync(payload);
        return true;
    }

    public async Task EnqueueAsync(string payload)
    {
        string kinds;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            kinds = doc.RootElement.ValueKind == JsonValueKind.Object
                ? string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name)) : "";
        }
        catch (JsonException) { kinds = "(nieczytelne)"; }
        await using var db = await dbFactory.CreateDbContextAsync();
        db.GarminInbox.Add(new GarminInboxItem { Payload = payload, Kinds = kinds.Length > 200 ? kinds[..200] : kinds });
        await db.SaveChangesAsync();
    }

    /// <summary>Przekazanie zaległych paczek do studiów. Paczka, której studio nie przyjęło, czeka na kolejną próbę.</summary>
    public async Task<int> ProcessInboxAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.GarminInbox.Where(i => i.ProcessedAt != null && i.ProcessedAt < DateTime.UtcNow.AddDays(-7)).ExecuteDeleteAsync(ct);
        var items = await db.GarminInbox.Where(i => i.ProcessedAt == null && i.Attempts < 12).OrderBy(i => i.Id).Take(25).ToListAsync(ct);
        var done = 0;
        foreach (var item in items)
        {
            // Ponawianie z odstępem: 1, 2, 4… minut (do ok. godziny).
            if (item.Attempts > 0 && item.ReceivedAt.AddMinutes(Math.Pow(2, Math.Min(item.Attempts, 6))) > DateTime.UtcNow) continue;
            item.Attempts++;
            try
            {
                item.Error = await ForwardAsync(db, item.Payload, ct);
                if (item.Error is null) { item.ProcessedAt = DateTime.UtcNow; done++; }
            }
            catch (Exception ex) { item.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message; }
            await db.SaveChangesAsync(ct);
        }
        return done;
    }

    /// <summary>Jedna paczka → studia. Null — wszystko przekazane (albo nie było komu); tekst — błąd do ponowienia.</summary>
    private async Task<string?> ForwardAsync(PortalDbContext db, string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var conns = await db.GarminConnections.Include(c => c.Tenant).Where(c => c.DisconnectedAt == null).ToListAsync(ct);
        var byUser = conns.ToDictionary(c => c.GarminUserId);
        var errors = new List<string>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            if (prop.Name is "deregistrations" or "userPermissionsChange")
            {
                foreach (var r in prop.Value.EnumerateArray())
                {
                    if (UserOf(r) is not { } uid || !byUser.TryGetValue(uid, out var c)) continue;
                    var perms = r.TryGetProperty("permissions", out var p) && p.ValueKind == JsonValueKind.Array
                        ? p.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).ToList() : null;
                    if (prop.Name == "userPermissionsChange" && perms is { Count: > 0 }) { c.Permissions = string.Join(",", perms); continue; }
                    // Odłączenie w Garmin Connect (albo odebranie wszystkich uprawnień) = koniec przekazywania danych.
                    c.DisconnectedAt = DateTime.UtcNow;
                    c.AccessTokenProtected = c.RefreshTokenProtected = null;
                    if (c.Tenant is { } t) await NotifyLinkAsync(t, c.TenantClientId, false);
                }
                continue;
            }
            if (!ForwardKinds.Contains(prop.Name)) continue;
            foreach (var group in prop.Value.EnumerateArray().Where(r => UserOf(r) is { } u && byUser.ContainsKey(u)).GroupBy(r => UserOf(r)!))
            {
                var c = byUser[group.Key];
                if (c.Tenant is null) continue;
                var ok = await PostToTenantAsync(c.Tenant, "/internal/garmin/data",
                    new { clientId = c.TenantClientId, kind = prop.Name, records = group.ToList() }, ct);
                if (ok) c.LastDataAt = DateTime.UtcNow;
                else errors.Add($"{c.Tenant.Slug}: {prop.Name}");
            }
        }
        await db.SaveChangesAsync(ct);
        return errors.Count == 0 ? null : "Studio nie przyjęło: " + string.Join(", ", errors.Distinct().Take(5));
    }

    private static string? UserOf(JsonElement r) =>
        r.ValueKind == JsonValueKind.Object && r.TryGetProperty("userId", out var u)
            ? u.ValueKind == JsonValueKind.String ? u.GetString() : u.GetRawText() : null;

    private async Task NotifyLinkAsync(Tenant tenant, int clientId, bool connected) =>
        await PostToTenantAsync(tenant, "/internal/garmin/link", new { clientId, connected }, default);

    private async Task<bool> PostToTenantAsync(Tenant tenant, string path, object body, CancellationToken ct)
    {
        var secret = TenantSecrets.For(tenant, config);
        if (string.IsNullOrEmpty(secret)) return false;
        try
        {
            var baseUrl = await TenantEndpoint.BaseUrlAsync(config, tenant);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{path}")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Internal-Secret", secret);
            using var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Przekazanie danych Garmina do {Slug} nie powiodło się.", tenant.Slug);
            return false;
        }
    }

    // ── Tryb testowy (bez prawdziwego konta Garmin) ──

    /// <summary>Połączenie testowe: klient studia „ma” zegarek, a dane przychodzą z przycisku w panelu.</summary>
    public async Task<string> ConnectTestAsync(Tenant tenant, int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var userId = $"test-{tenant.Slug}-{clientId}";
        var conn = await db.GarminConnections.FirstOrDefaultAsync(c => c.GarminUserId == userId);
        if (conn is null) db.GarminConnections.Add(conn = new GarminConnection { GarminUserId = userId });
        conn.TenantId = tenant.Id;
        conn.TenantClientId = clientId;
        conn.IsTest = true;
        conn.ConnectedAt = DateTime.UtcNow;
        conn.DisconnectedAt = null;
        await db.SaveChangesAsync();
        await NotifyLinkAsync(tenant, clientId, true);
        return userId;
    }

    /// <summary>Przykładowe dane z ostatnich dni (sen, tętno, HRV, Body Battery, aktywności) w formacie Garmina — do kolejki.</summary>
    public async Task EnqueueSampleAsync(string garminUserId, int days, DateOnly today)
    {
        var rnd = new Random(garminUserId.GetHashCode());
        var dailies = new List<object>();
        var sleeps = new List<object>();
        var hrv = new List<object>();
        var activities = new List<object>();
        for (var i = days - 1; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            var tired = i == 0; // dziś gorsza noc — gotowość „niska/średnia” do sprawdzenia w aplikacji
            var date = d.ToString("yyyy-MM-dd");
            dailies.Add(new
            {
                userId = garminUserId, summaryId = $"d-{garminUserId}-{date}", calendarDate = date,
                steps = rnd.Next(5000, 13000), restingHeartRateInBeatsPerMinute = tired ? 61 : rnd.Next(52, 56),
                averageStressLevel = rnd.Next(20, 45), bodyBatteryHighestValue = tired ? 34 : rnd.Next(65, 95),
                bodyBatteryLowestValue = rnd.Next(10, 30), activeKilocalories = rnd.Next(300, 900)
            });
            var total = tired ? 310 * 60 : rnd.Next(400, 500) * 60;
            sleeps.Add(new
            {
                userId = garminUserId, summaryId = $"s-{garminUserId}-{date}", calendarDate = date, durationInSeconds = total,
                deepSleepDurationInSeconds = total / 5, lightSleepDurationInSeconds = total / 2, remSleepInSeconds = total / 5,
                awakeDurationInSeconds = total / 10, overallSleepScore = new { value = tired ? 52 : rnd.Next(70, 90) }
            });
            hrv.Add(new { userId = garminUserId, summaryId = $"h-{garminUserId}-{date}", calendarDate = date, lastNightAvg = tired ? 38 : rnd.Next(48, 58) });
            if (i % 2 == 1)
            {
                var start = new DateTimeOffset(d.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero).ToUnixTimeSeconds();
                var run = i % 4 == 1;
                activities.Add(new
                {
                    userId = garminUserId, summaryId = $"a-{garminUserId}-{date}", activityId = 1000 + i,
                    activityName = run ? "Bieg poranny" : "Trening siłowy", activityType = run ? "RUNNING" : "STRENGTH_TRAINING",
                    startTimeInSeconds = start, durationInSeconds = run ? 2400 : 3300,
                    averageHeartRateInBeatsPerMinute = run ? 148 : 112, maxHeartRateInBeatsPerMinute = run ? 171 : 152,
                    activeKilocalories = run ? 420 : 310, distanceInMeters = run ? 6200 : 0, deviceName = "Forerunner 265"
                });
            }
        }
        await EnqueueAsync(JsonSerializer.Serialize(new { dailies, sleeps, hrv, activities }));
    }

    // ── Pomocnicze ──

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string NewKey(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        return string.Create(length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Tylko ścieżka w aplikacji studia (bez adresu) — powrót zawsze na adres tego studia.</summary>
    public static string SafePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && !path.StartsWith("//") && !path.StartsWith("/\\") && path.Length <= 200
            ? path : "/garmin";

    private static string Append(string url, string query) => url + (url.Contains('?') ? "&" : "?") + query;
}

/// <summary>Przekazywanie danych z Garmina do studiów w tle (co 20 s).</summary>
public sealed class GarminBackgroundService(IServiceScopeFactory scopes, ILogger<GarminBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GarminService>().ProcessInboxAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Przekazywanie danych Garmina nie powiodło się."); }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }
}
