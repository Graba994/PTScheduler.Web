using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PTScheduler.Portal.Services;

public class NpmService(SiteSettingsService settings, ILogger<NpmService> logger)
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    // Wydanie certyfikatu trwa dłużej — limit czasu ustawiamy osobno dla każdego zapytania.
    private static readonly HttpClient _slowHttp = new() { Timeout = TimeSpan.FromMinutes(3) };
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

    /// <summary>
    /// Zakłada (albo aktualizuje) proxy host dla domeny. Gdy włączone jest automatyczne HTTPS
    /// (domyślnie tak), Portal używa istniejącego certyfikatu obejmującego domenę (także wildcard
    /// *.domena) albo prosi Let's Encrypt o nowy, a potem wymusza HTTPS i HSTS.
    /// Bez certyfikatu host i tak działa po http — komunikat mówi, co poprawić (DNS, port 80).
    /// </summary>
    public async Task<(bool Success, string Message)> RegisterProxyHostAsync(
        string domain, string forwardHost, int forwardPort, bool? ssl = null)
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.NpmUrl, SiteSettingsService.Keys.NpmAutoSsl);
        var url = s[SiteSettingsService.Keys.NpmUrl]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
            return (false, "NPM nie skonfigurowany.");
        var wantSsl = ssl ?? s[SiteSettingsService.Keys.NpmAutoSsl] != "false";

        var token = await GetTokenAsync();
        if (token is null) return (false, "Nie udało się zalogować do NPM.");

        try
        {
            // 1) Host po http (potrzebny też do weryfikacji domeny przez Let's Encrypt).
            var existing = await FindHostAsync(url, token, domain);
            var hostId = existing?.GetProperty("id").GetInt32();
            if (hostId is null)
            {
                var (ok, body) = await SendAsync(url, token, HttpMethod.Post, "/api/nginx/proxy-hosts", HostPayload(domain, forwardHost, forwardPort, 0, false));
                if (!ok) return (false, $"NPM: {body}");
                hostId = JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
            }
            if (!wantSsl) return (true, $"Zarejestrowano proxy host dla {domain} (bez HTTPS)");

            // 2) Certyfikat: istniejący albo nowy z Let's Encrypt.
            var (certId, certMsg) = await EnsureCertificateAsync(url, token, domain);
            if (certId is null)
                return (true, $"Proxy host dla {domain} działa po http, ale certyfikat nie powstał: {certMsg}");

            // 3) Host z certyfikatem, wymuszonym HTTPS i HSTS.
            var (updated, updBody) = await SendAsync(url, token, HttpMethod.Put, $"/api/nginx/proxy-hosts/{hostId}",
                HostPayload(domain, forwardHost, forwardPort, certId.Value, true));
            return updated
                ? (true, $"Zarejestrowano {domain} z HTTPS ({certMsg})")
                : (true, $"Proxy host dla {domain} działa, ale nie udało się włączyć HTTPS: {updBody}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NPM proxy host creation failed for {Domain}", domain);
            return (false, ex.Message);
        }
    }

    /// <summary>Naprawia HTTPS wszystkich hostów: wystawia brakujące certyfikaty, odnawia wygasające, wymusza HTTPS.</summary>
    public async Task<List<(string Domain, bool Ok, string Message)>> EnableHttpsForAllAsync(IReadOnlyCollection<string> trainerDomains)
    {
        // Tylko włączone wpisy domen trenerów — inne usługi w tym samym NPM (i celowo wyłączone wpisy) zostają nietknięte.
        var only = new HashSet<string>(trainerDomains, StringComparer.OrdinalIgnoreCase);
        var results = new List<(string, bool, string)>();
        foreach (var h in (await ListProxyHostsAsync()).Where(h => h.Enabled && h.HttpsState != NpmHttpsState.Ok && only.Contains(h.Domain)))
        {
            // Wygasający albo wygasły Let's Encrypt — odnowienie; brak certyfikatu — wystawienie i HTTPS.
            if (h.CertificateId > 0 && h.CertProvider == "letsencrypt" && h.HttpsState is NpmHttpsState.Expiring or NpmHttpsState.Expired)
            {
                var (renewed, renewMsg) = await RenewCertificateAsync(h.Id);
                results.Add((h.Domain, renewed, renewMsg));
                continue;
            }
            var (ok, msg) = await RegisterProxyHostAsync(h.Domain, h.ForwardHost, h.ForwardPort, ssl: true);
            results.Add((h.Domain, ok && msg.Contains("z HTTPS"), msg));
        }
        return results;
    }

    private static object HostPayload(string domain, string forwardHost, int forwardPort, int certificateId, bool ssl) => new
    {
        domain_names = new[] { domain },
        forward_scheme = "http",
        forward_host = forwardHost,
        forward_port = forwardPort,
        block_exploits = true,
        allow_websocket_upgrade = true, // Blazor (SignalR) potrzebuje WebSocketów
        caching_enabled = false,
        access_list_id = 0,
        certificate_id = certificateId,
        ssl_forced = ssl,
        http2_support = ssl,
        hsts_enabled = ssl,
        hsts_subdomains = false,
        enabled = true,
        meta = new { letsencrypt_agree = false, dns_challenge = false },
        advanced_config = "",
        locations = Array.Empty<object>()
    };

    /// <summary>Id certyfikatu obejmującego domenę: istniejący (dokładny albo wildcard) albo nowy z Let's Encrypt.</summary>
    private async Task<(int? Id, string Message)> EnsureCertificateAsync(string url, string token, string domain)
    {
        var (ok, body) = await SendAsync(url, token, HttpMethod.Get, "/api/nginx/certificates", null);
        if (ok)
        {
            var parent = domain.Contains('.') ? domain[(domain.IndexOf('.') + 1)..] : "";
            foreach (var c in JsonSerializer.Deserialize<List<JsonElement>>(body, _json) ?? [])
            {
                if (!c.TryGetProperty("domain_names", out var names)) continue;
                var list = names.EnumerateArray().Select(n => n.GetString() ?? "").ToList();
                if (list.Any(n => n.Equals(domain, StringComparison.OrdinalIgnoreCase)
                                  || (parent.Length > 0 && n.Equals("*." + parent, StringComparison.OrdinalIgnoreCase))))
                {
                    var expired = c.TryGetProperty("expires_on", out var exp) && DateTime.TryParse(exp.GetString(), out var e) && e < DateTime.UtcNow;
                    if (!expired) return (c.GetProperty("id").GetInt32(), "istniejący certyfikat");
                }
            }
        }

        var s = await settings.GetAllAsync(SiteSettingsService.Keys.AdminNotificationEmail, SiteSettingsService.Keys.NpmEmail);
        var email = s[SiteSettingsService.Keys.AdminNotificationEmail] is { Length: > 0 } a ? a : s[SiteSettingsService.Keys.NpmEmail];
        var payload = new
        {
            provider = "letsencrypt",
            nice_name = domain,
            domain_names = new[] { domain },
            meta = new { letsencrypt_email = email, letsencrypt_agree = true, dns_challenge = false }
        };
        // Let's Encrypt weryfikuje domenę przez http — to trwa do minuty.
        var (created, certBody) = await SendAsync(url, token, HttpMethod.Post, "/api/nginx/certificates", payload, TimeSpan.FromSeconds(120));
        if (!created)
        {
            logger.LogWarning("Let's Encrypt dla {Domain} nie powiódł się: {Body}", domain, certBody);
            return (null, "Let's Encrypt odmówił — sprawdź, czy domena wskazuje na ten serwer (rekord A) i czy port 80 jest otwarty. " + Short(certBody));
        }
        return (JsonDocument.Parse(certBody).RootElement.GetProperty("id").GetInt32(), "nowy certyfikat Let's Encrypt");
    }

    private async Task<JsonElement?> FindHostAsync(string url, string token, string domain)
    {
        var (ok, body) = await SendAsync(url, token, HttpMethod.Get, "/api/nginx/proxy-hosts", null);
        if (!ok) return null;
        foreach (var h in JsonSerializer.Deserialize<List<JsonElement>>(body, _json) ?? [])
            if (h.TryGetProperty("domain_names", out var names)
                && names.EnumerateArray().Any(n => string.Equals(n.GetString(), domain, StringComparison.OrdinalIgnoreCase)))
                return h;
        return null;
    }

    private static async Task<(bool Ok, string Body)> SendAsync(string url, string token, HttpMethod method, string path, object? payload, TimeSpan? timeout = null)
    {
        using var req = new HttpRequestMessage(method, url + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        using var resp = await _slowHttp.SendAsync(req, cts.Token);
        var body = await resp.Content.ReadAsStringAsync(cts.Token);
        return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? body : $"{(int)resp.StatusCode}: {Short(body)}");
    }

    private static string Short(string s) => s.Length > 300 ? s[..300] + "…" : s;

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

    public async Task<List<NpmProxyHost>> ListProxyHostsAsync() => (await ListProxyHostsDetailedAsync()).Hosts;

    /// <summary>
    /// Proxy hosty razem z certyfikatami. Zamiast pustej listy przy kłopocie zwraca opis błędu —
    /// wcześniej każdy wyjątek (np. inny typ pola w nowszej wersji NPM) dawał „brak wpisów”.
    /// </summary>
    public async Task<(List<NpmProxyHost> Hosts, string? Error)> ListProxyHostsDetailedAsync()
    {
        var url = (await settings.GetAsync(SiteSettingsService.Keys.NpmUrl))?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url)) return ([], "NPM nie jest skonfigurowany.");

        var token = await GetTokenAsync();
        if (token is null) return ([], "Nie udało się zalogować do NPM — sprawdź adres, e-mail i hasło.");

        try
        {
            var (ok, body) = await SendAsync(url, token, HttpMethod.Get, "/api/nginx/proxy-hosts?expand=certificate", null, TimeSpan.FromSeconds(20));
            if (!ok) return ([], $"NPM odrzucił zapytanie o listę: {body}");
            var hosts = new List<NpmProxyHost>();
            foreach (var h in JsonSerializer.Deserialize<List<JsonElement>>(body, _json) ?? [])
            {
                try { hosts.Add(ParseHost(h)); }
                catch (Exception ex) { logger.LogWarning(ex, "Pominięto nieczytelny wpis NPM: {Json}", Short(h.GetRawText())); }
            }
            return (hosts.OrderBy(x => x.Domain, StringComparer.OrdinalIgnoreCase).ToList(), null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nie udało się pobrać listy proxy hostów z NPM.");
            return ([], $"Nie udało się pobrać listy z NPM: {ex.Message}");
        }
    }

    private static NpmProxyHost ParseHost(JsonElement h)
    {
        var host = new NpmProxyHost
        {
            Id = Int(h, "id"),
            Domain = h.TryGetProperty("domain_names", out var names) && names.ValueKind == JsonValueKind.Array
                ? names.EnumerateArray().Select(n => n.GetString() ?? "").FirstOrDefault(n => n.Length > 0) ?? "" : "",
            ForwardHost = Str(h, "forward_host"),
            ForwardPort = Int(h, "forward_port"),
            SslForced = Flag(h, "ssl_forced"),
            Enabled = !h.TryGetProperty("enabled", out _) || Flag(h, "enabled"),
            CertificateId = Int(h, "certificate_id")
        };
        if (h.TryGetProperty("certificate", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            host.CertProvider = Str(c, "provider");
            host.CertName = Str(c, "nice_name");
            if (DateTime.TryParse(Str(c, "expires_on"), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var exp))
                host.CertExpiresUtc = exp;
        }
        return host;
    }

    // NPM w różnych wersjach zwraca flagi jako true/false albo 1/0, a liczby czasem jako tekst.
    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n != 0)
            || (v.ValueKind == JsonValueKind.String && (v.GetString() is "1" or "true")));

    private static int Int(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? 0
        : v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n
        : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s) ? s : 0;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>
    /// Odnawia certyfikat hosta (Let's Encrypt), a gdy hosta nie ma certyfikatu — wystawia go i włącza HTTPS.
    /// Na końcu dopilnowuje wymuszonego HTTPS.
    /// </summary>
    public async Task<(bool Success, string Message)> RenewCertificateAsync(int hostId)
    {
        var url = (await settings.GetAsync(SiteSettingsService.Keys.NpmUrl))?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url)) return (false, "NPM nie jest skonfigurowany.");
        var (hosts, error) = await ListProxyHostsDetailedAsync();
        if (error is not null) return (false, error);
        var host = hosts.FirstOrDefault(h => h.Id == hostId);
        if (host is null) return (false, "Nie ma już tego wpisu w NPM — odśwież listę.");

        if (host.CertificateId == 0 || host.HttpsState == NpmHttpsState.Expired && host.CertProvider != "letsencrypt")
        {
            var (ok, msg) = await RegisterProxyHostAsync(host.Domain, host.ForwardHost, host.ForwardPort, ssl: true);
            return (ok && msg.Contains("z HTTPS"), msg);
        }
        if (host.CertProvider != "letsencrypt")
            return (false, $"{host.Domain} ma własny certyfikat (nie Let's Encrypt) — wgraj nowy w panelu NPM.");

        var token = await GetTokenAsync();
        if (token is null) return (false, "Nie udało się zalogować do NPM.");
        try
        {
            var (renewed, body) = await SendAsync(url, token, HttpMethod.Post, $"/api/nginx/certificates/{host.CertificateId}/renew", null, TimeSpan.FromSeconds(150));
            if (!renewed)
                return (false, $"Let's Encrypt nie odnowił certyfikatu {host.Domain} — sprawdź, czy domena wskazuje na ten serwer i czy port 80 jest otwarty. {Short(body)}");
            if (!host.SslForced)
                await SendAsync(url, token, HttpMethod.Put, $"/api/nginx/proxy-hosts/{host.Id}",
                    HostPayload(host.Domain, host.ForwardHost, host.ForwardPort, host.CertificateId, true));
            DateTime? expires = null;
            try { expires = JsonDocument.Parse(body).RootElement.TryGetProperty("expires_on", out var e) && DateTime.TryParse(e.GetString(), out var d) ? d : null; } catch { }
            return (true, $"Odnowiono certyfikat {host.Domain}" + (expires is { } x ? $" — ważny do {x:dd.MM.yyyy}." : "."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Odnowienie certyfikatu {Domain} nie powiodło się.", host.Domain);
            return (false, ex.Message);
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

public enum NpmHttpsState { Ok, NotForced, Expiring, Expired, None }

public class NpmProxyHost
{
    /// <summary>Let's Encrypt w NPM odnawia certyfikat ok. 30 dni przed końcem — 14 dni przed to znak, że odnawianie nie działa.</summary>
    public const int ExpiringDays = 14;

    public int Id { get; set; }
    public string Domain { get; set; } = "";
    public string ForwardHost { get; set; } = "";
    public int ForwardPort { get; set; }
    public bool SslForced { get; set; }
    public bool Enabled { get; set; }
    public int CertificateId { get; set; }
    public string CertProvider { get; set; } = "";
    public string CertName { get; set; } = "";
    public DateTime? CertExpiresUtc { get; set; }

    public int? DaysLeft => CertExpiresUtc is { } e ? (int)Math.Floor((e - DateTime.UtcNow).TotalDays) : null;

    public NpmHttpsState HttpsState =>
        CertificateId == 0 ? NpmHttpsState.None
        : CertExpiresUtc is { } e && e <= DateTime.UtcNow ? NpmHttpsState.Expired
        : DaysLeft is < ExpiringDays ? NpmHttpsState.Expiring
        : !SslForced ? NpmHttpsState.NotForced
        : NpmHttpsState.Ok;
}
