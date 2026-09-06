using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PTScheduler.Portal.Services;

public class NpmService(SiteSettingsService settings, ILogger<NpmService> logger)
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<NpmTestResult> TestConnectionAsync(string url, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return new NpmTestResult(false, "Uzupełnij URL, email i hasło.");

        try
        {
            var (ok, token, err) = await LoginAsync(url, email, password);
            if (!ok) return new NpmTestResult(false, $"Logowanie nie powiodło się: {err}");

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{url.TrimEnd('/')}/api/nginx/proxy-hosts");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return new NpmTestResult(false, $"NPM zwrócił {(int)resp.StatusCode}: {body}");

            var hosts = JsonSerializer.Deserialize<List<JsonElement>>(body, _json) ?? new();
            return new NpmTestResult(true, $"Połączono. NPM ma {hosts.Count} proxy hostów.");
        }
        catch (Exception ex)
        {
            return new NpmTestResult(false, ex.Message);
        }
    }

    public async Task<string?> GetTokenAsync()
    {
        var s = await settings.GetAllAsync(
            SiteSettingsService.Keys.NpmUrl,
            SiteSettingsService.Keys.NpmEmail,
            SiteSettingsService.Keys.NpmPassword);

        var url = s[SiteSettingsService.Keys.NpmUrl];
        var email = s[SiteSettingsService.Keys.NpmEmail];
        var password = s[SiteSettingsService.Keys.NpmPassword];

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        var (ok, token, _) = await LoginAsync(url, email, password);
        return ok ? token : null;
    }

    public async Task<(bool Success, string Message)> RegisterProxyHostAsync(
        string domain, string forwardHost, int forwardPort, bool ssl = false)
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.NpmUrl);
        var url = s[SiteSettingsService.Keys.NpmUrl];
        if (string.IsNullOrWhiteSpace(url))
            return (false, "NPM nie skonfigurowany.");

        var token = await GetTokenAsync();
        if (token is null) return (false, "Nie udało się zalogować do NPM.");

        var payload = new
        {
            domain_names = new[] { domain },
            forward_scheme = "http",
            forward_host = forwardHost,
            forward_port = forwardPort,
            block_exploits = true,
            allow_websocket_upgrade = true,
            ssl_forced = ssl,
            http2_support = true,
            hsts_enabled = false,
            hsts_subdomains = false,
            enabled = true,
            meta = new { letsencrypt_agree = ssl, dns_challenge = false },
            advanced_config = "",
            locations = Array.Empty<object>(),
            certificate_id = ssl ? (object)"new" : 0
        };

        var json = JsonSerializer.Serialize(payload, _json);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{url.TrimEnd('/')}/api/nginx/proxy-hosts")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (false, $"NPM {(int)resp.StatusCode}: {body}");

            return (true, $"Zarejestrowano proxy host dla {domain}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NPM proxy host creation failed for {Domain}", domain);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> DeleteProxyHostByDomainAsync(string domain)
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.NpmUrl);
        var url = s[SiteSettingsService.Keys.NpmUrl];
        if (string.IsNullOrWhiteSpace(url)) return (false, "NPM nie skonfigurowany.");

        var token = await GetTokenAsync();
        if (token is null) return (false, "Nie udało się zalogować do NPM.");

        try
        {
            using var listReq = new HttpRequestMessage(HttpMethod.Get, $"{url.TrimEnd('/')}/api/nginx/proxy-hosts");
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var listResp = await _http.SendAsync(listReq);
            if (!listResp.IsSuccessStatusCode) return (false, "Nie udało się pobrać listy proxy hostów");

            var body = await listResp.Content.ReadAsStringAsync();
            var hosts = JsonSerializer.Deserialize<List<JsonElement>>(body, _json) ?? new();

            var match = hosts.FirstOrDefault(h =>
                h.TryGetProperty("domain_names", out var names) &&
                names.EnumerateArray().Any(n => string.Equals(n.GetString(), domain, StringComparison.OrdinalIgnoreCase)));

            if (match.ValueKind == JsonValueKind.Undefined) return (true, $"Nie znaleziono {domain} w NPM (już usunięty?)");

            var id = match.GetProperty("id").GetInt32();
            using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"{url.TrimEnd('/')}/api/nginx/proxy-hosts/{id}");
            delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var delResp = await _http.SendAsync(delReq);
            if (!delResp.IsSuccessStatusCode) return (false, $"Usuwanie zwróciło {(int)delResp.StatusCode}");

            return (true, $"Usunięto proxy host dla {domain}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<List<NpmProxyHost>> ListProxyHostsAsync()
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.NpmUrl);
        var url = s[SiteSettingsService.Keys.NpmUrl];
        if (string.IsNullOrWhiteSpace(url)) return new();

        var token = await GetTokenAsync();
        if (token is null) return new();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{url.TrimEnd('/')}/api/nginx/proxy-hosts");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new();

            var body = await resp.Content.ReadAsStringAsync();
            var hosts = JsonSerializer.Deserialize<List<JsonElement>>(body, _json) ?? new();

            return hosts.Select(h => new NpmProxyHost
            {
                Id = h.GetProperty("id").GetInt32(),
                Domain = h.GetProperty("domain_names").EnumerateArray().FirstOrDefault().GetString() ?? "",
                ForwardHost = h.GetProperty("forward_host").GetString() ?? "",
                ForwardPort = h.GetProperty("forward_port").GetInt32(),
                SslForced = h.TryGetProperty("ssl_forced", out var f) && f.ValueKind == JsonValueKind.True,
                Enabled = h.TryGetProperty("enabled", out var e) && e.GetInt32() == 1
            }).ToList();
        }
        catch
        {
            return new();
        }
    }

    private async Task<(bool Success, string? Token, string? Error)> LoginAsync(string url, string email, string password)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { identity = email, secret = password }, _json);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{url.TrimEnd('/')}/api/tokens")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (false, null, $"{(int)resp.StatusCode}: {body}");

            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("token", out var t))
                return (true, t.GetString(), null);

            return (false, null, "Brak tokenu w odpowiedzi");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }
}

public record NpmTestResult(bool Success, string Message);

public class NpmProxyHost
{
    public int Id { get; set; }
    public string Domain { get; set; } = "";
    public string ForwardHost { get; set; } = "";
    public int ForwardPort { get; set; }
    public bool SslForced { get; set; }
    public bool Enabled { get; set; }
}
