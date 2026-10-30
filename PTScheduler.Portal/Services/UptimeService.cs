using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>Wynik jednego sprawdzenia adresu.</summary>
public sealed record UptimeCheck(bool Up, int? Ms, int? StatusCode, string? Error, DateTime? CertExpiresAt);

/// <summary>Co zrobić po sprawdzeniu: nowy stan i czy zaczęła się albo skończyła awaria.</summary>
public sealed record UptimeTransition(UptimeState State, bool WentDown, bool CameBack, int FailuresInRow);

/// <summary>Reguły monitora bez bazy i sieci — testowane osobno.</summary>
public static class UptimeRules
{
    /// <summary>Ile dni przed końcem certyfikatu ostrzegamy.</summary>
    public const int CertWarnDays = 14;
    public static readonly TimeSpan BeatsKept = TimeSpan.FromDays(45);
    public static readonly int[] Intervals = [30, 60, 120, 300];

    /// <summary>
    /// Awaria dopiero po <see cref="UptimeMonitor.RetriesBeforeDown"/> nieudanych próbach z rzędu;
    /// jedna udana próba kończy awarię.
    /// </summary>
    public static UptimeTransition Next(UptimeState current, int failuresInRow, int retriesBeforeDown, bool up)
    {
        if (up) return new(UptimeState.Up, false, current == UptimeState.Down, 0);
        var failures = failuresInRow + 1;
        if (current == UptimeState.Down) return new(UptimeState.Down, false, false, failures);
        return failures >= Math.Max(1, retriesBeforeDown)
            ? new(UptimeState.Down, true, false, failures)
            // Przed progiem stan zostaje (Up albo Pending) — pasek pokaże czerwoną kreskę, alarmu jeszcze nie ma.
            : new(current, false, false, failures);
    }

    /// <summary>Uptime w procentach (null — brak danych).</summary>
    public static double? Percent(int up, int total) => total == 0 ? null : Math.Round(100.0 * up / total, 2);

    public static string Show(double? percent) => percent is null ? "—" : percent >= 99.995 ? "100%" : $"{percent:0.##}%";

    /// <summary>Średni czas odpowiedzi w równych przedziałach — do wykresu (null, gdy w przedziale nie było udanych prób).</summary>
    public static List<(DateTime From, int? AvgMs, bool AnyDown)> Buckets(IEnumerable<UptimeBeat> beats, DateTime from, DateTime to, int count)
    {
        var size = (to - from) / count;
        var list = beats.Where(b => b.At >= from && b.At < to).ToList();
        return Enumerable.Range(0, count).Select(i =>
        {
            var start = from + size * i;
            var inBucket = list.Where(b => b.At >= start && b.At < start + size).ToList();
            var ok = inBucket.Where(b => b.Up && b.Ms is not null).Select(b => b.Ms!.Value).ToList();
            return (start, ok.Count == 0 ? (int?)null : (int)ok.Average(), inBucket.Any(b => !b.Up));
        }).ToList();
    }

    /// <summary>Adres monitora aplikacji trenera — publiczny, przez proxy i SSL.</summary>
    public static string TenantUrl(Tenant t) => $"https://{(t.CustomDomainStatus == "active" && !string.IsNullOrWhiteSpace(t.CustomDomain) ? t.CustomDomain : t.Domain)}/health";

    public static string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Wpisz adres.";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            return "Adres musi zaczynać się od https:// albo http://.";
        return null;
    }

    public static string Ago(DateTime? at, DateTime now)
    {
        if (at is null) return "—";
        var d = now - at.Value;
        return d.TotalMinutes < 1 ? "przed chwilą" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"{(int)d.TotalHours} godz." : $"{(int)d.TotalDays} dni";
    }
}

/// <summary>Monitory dostępności: sprawdzanie, zapis wyników, awarie i alarmy, statystyki do panelu i strony /status.</summary>
public class UptimeService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    EmailService email,
    CreditService credits,
    IConfiguration config,
    ILogger<UptimeService> logger)
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            SslOptions = new SslClientAuthenticationOptions
            {
                // Zapamiętujemy datę końca certyfikatu; sprawdzenie poprawności zostaje domyślne.
                RemoteCertificateValidationCallback = (req, cert, chain, errors) =>
                {
                    if (cert is X509Certificate2 c2 && req is SslStream { TargetHostName: { } host }) CertCache[host] = c2.NotAfter.ToUniversalTime();
                    return errors == SslPolicyErrors.None;
                }
            }
        };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HantlyUptime/1.0");
        return http;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> CertCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<UptimeCheck> ProbeAsync(string url, string? keyword, int timeoutSeconds, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 2, 60)));
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var ms = (int)sw.ElapsedMilliseconds;
            var code = (int)resp.StatusCode;
            DateTime? cert = Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && CertCache.TryGetValue(u.Host, out var c) ? c : null;
            if (code is < 200 or >= 400) return new(false, ms, code, $"Odpowiedź {code} {resp.ReasonPhrase}", cert);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                if (!body.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return new(false, ms, code, $"Brak tekstu „{keyword}” na stronie", cert);
            }
            return new(true, ms, code, null, cert);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, null, $"Brak odpowiedzi w {timeoutSeconds} s", null);
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.InnerException is System.Security.Authentication.AuthenticationException ? "Błąd certyfikatu SSL"
                : ex.HttpRequestError switch
                {
                    HttpRequestError.NameResolutionError => "Domena nie istnieje albo DNS nie odpowiada",
                    HttpRequestError.ConnectionError => "Nie można połączyć się z serwerem",
                    HttpRequestError.SecureConnectionError => "Błąd połączenia SSL",
                    _ => ex.Message
                };
            return new(false, null, null, reason, null);
        }
    }

    /// <summary>Monitory aplikacji trenerów: dodaje brakujące, poprawia adres po zmianie domeny, wyłącza usunięte.</summary>
    public async Task SyncTenantsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active && t.Domain != "").ToListAsync(ct);
        var monitors = await db.UptimeMonitors.Where(m => m.TenantId != null).ToListAsync(ct);
        foreach (var t in tenants)
        {
            var url = UptimeRules.TenantUrl(t);
            var m = monitors.FirstOrDefault(x => x.TenantId == t.Id);
            if (m is null)
                db.UptimeMonitors.Add(new UptimeMonitor { TenantId = t.Id, Name = string.IsNullOrWhiteSpace(t.CompanyName) ? t.Slug : t.CompanyName, Url = url, Keyword = null, SortOrder = 100 });
            else if (m.Url != url) m.Url = url;
        }
        foreach (var m in monitors.Where(m => tenants.All(t => t.Id != m.TenantId) && !m.IsPaused))
        {
            m.IsPaused = true;
            m.State = UptimeState.Paused;
        }
        if (!await db.UptimeMonitors.AnyAsync(m => m.TenantId == null, ct))
        {
            var main = (await settings.GetAsync(SiteSettingsService.Keys.MainDomain)).Trim();
            if (main.Length > 0)
                db.UptimeMonitors.Add(new UptimeMonitor { Name = "Strona główna", Url = $"https://{main}/", IsPublic = true, SortOrder = 0 });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sprawdza monitory, którym minął interwał (wywołuje usługa w tle co kilka sekund).</summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var due = (await db.UptimeMonitors.AsNoTracking().Where(m => !m.IsPaused).ToListAsync(ct))
            .Where(m => m.LastCheckAt is null || m.LastCheckAt.Value.AddSeconds(Math.Max(20, m.IntervalSeconds) - 2) <= now)
            .ToList();
        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(due.Select(async m =>
        {
            await gate.WaitAsync(ct);
            try { await CheckAsync(m.Id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Monitor {Name}: sprawdzenie nie powiodło się.", m.Name); }
            finally { gate.Release(); }
        }));
        return due.Count;
    }

    /// <summary>Jedno sprawdzenie monitora z zapisem wyniku, zmianą stanu i alarmem.</summary>
    public async Task<UptimeCheck?> CheckAsync(int monitorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.UptimeMonitors.FirstOrDefaultAsync(x => x.Id == monitorId, ct);
        if (m is null) return null;
        var r = await ProbeAsync(m.Url, m.Keyword, m.TimeoutSeconds, ct);
        var now = DateTime.UtcNow;
        var t = UptimeRules.Next(m.State == UptimeState.Paused ? UptimeState.Pending : m.State, m.FailuresInRow, m.RetriesBeforeDown, r.Up);

        db.UptimeBeats.Add(new UptimeBeat { MonitorId = m.Id, At = now, Up = r.Up, Ms = r.Ms, StatusCode = r.StatusCode, Error = Trim(r.Error, 300) });
        if (t.State != m.State) m.StateSince = now;
        m.State = t.State;
        m.FailuresInRow = t.FailuresInRow;
        m.LastCheckAt = now;
        m.LastResponseMs = r.Ms;
        m.LastError = Trim(r.Error, 500);
        if (r.CertExpiresAt is not null) m.CertExpiresAt = r.CertExpiresAt;

        UptimeIncident? ended = null;
        if (t.WentDown)
            db.UptimeIncidents.Add(new UptimeIncident { MonitorId = m.Id, StartedAt = now, Error = Trim(r.Error, 500) });
        if (t.CameBack)
        {
            ended = await db.UptimeIncidents.Where(i => i.MonitorId == m.Id && i.EndedAt == null).OrderByDescending(i => i.StartedAt).FirstOrDefaultAsync(ct);
            if (ended is not null) ended.EndedAt = now;
        }
        var certWarn = m.CertExpiresAt is { } exp && exp - now < TimeSpan.FromDays(UptimeRules.CertWarnDays) && m.CertAlertSentOn?.Date != now.Date;
        if (certWarn) m.CertAlertSentOn = now.Date;
        await db.SaveChangesAsync(ct);

        if (t.WentDown) await AlertAsync(db, m, down: true, r.Error, null);
        if (t.CameBack) await AlertAsync(db, m, down: false, null, ended is null ? null : now - ended.StartedAt);
        if (certWarn) await CertAlertAsync(m);
        return r;
    }

    private async Task AlertAsync(PortalDbContext db, UptimeMonitor m, bool down, string? error, TimeSpan? downtime)
    {
        // Aplikacja trenera, której awarię już zgłosił wewnętrzny monitor kontenerów — bez drugiego e-maila.
        if (m.TenantId is int tid && await db.Tenants.AnyAsync(x => x.Id == tid && x.IsHealthy == false && x.DownAlertSent)) return;
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.AdminNotificationEmail, SiteSettingsService.Keys.AdminNotificationPhone);
        var to = s[SiteSettingsService.Keys.AdminNotificationEmail];
        var link = $"{PortalUrls.Public(config)}/panel/dostepnosc?m={m.Id}";
        var name = WebUtility.HtmlEncode(m.Name);
        var subject = down ? $"🔴 Nie działa: {m.Name}" : $"🟢 Znów działa: {m.Name}";
        var body = down
            ? $"<p><b>{name}</b> nie odpowiada od {DateTime.Now:HH:mm} ({m.RetriesBeforeDown} nieudane próby z rzędu).</p><p>Adres: {WebUtility.HtmlEncode(m.Url)}<br>Powód: {WebUtility.HtmlEncode(error ?? "—")}</p>"
            : $"<p><b>{name}</b> znów działa.</p><p>Przerwa: <b>{(downtime is { } d ? Duration(d) : "—")}</b></p>";
        body += $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Otwórz Dostępność w Portalu</a></p>";
        if (!string.IsNullOrWhiteSpace(to)) await email.SendAsync(to, subject, body);
        var phone = s[SiteSettingsService.Keys.AdminNotificationPhone];
        if (down && !string.IsNullOrWhiteSpace(phone) && await settings.GetAsync(SiteSettingsService.Keys.UptimeSmsAlerts) == "true")
            await credits.SendPlatformSmsAsync(phone, $"Hantly: nie dziala {m.Name} - {error}");
        logger.LogWarning("Dostępność: {Name} {State}.", m.Name, down ? "nie działa" : "znów działa");
    }

    private async Task CertAlertAsync(UptimeMonitor m)
    {
        var to = await settings.GetAsync(SiteSettingsService.Keys.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to) || m.CertExpiresAt is null) return;
        var days = (int)Math.Floor((m.CertExpiresAt.Value - DateTime.UtcNow).TotalDays);
        await email.SendAsync(to, $"Certyfikat SSL kończy się za {days} dni: {m.Name}",
            $"<p>Certyfikat <b>{WebUtility.HtmlEncode(m.Name)}</b> ({WebUtility.HtmlEncode(m.Url)}) wygasa {m.CertExpiresAt.Value.ToLocalTime():dd.MM.yyyy}.</p>" +
            "<p>Zwykle odnawia się sam (Nginx Proxy Manager / Let's Encrypt) — jeśli ta wiadomość wraca, sprawdź odnawianie w proxy.</p>");
    }

    public static string Duration(TimeSpan d) =>
        d.TotalMinutes < 1 ? $"{(int)d.TotalSeconds} s" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"{(int)d.TotalHours} godz. {d.Minutes} min" : $"{(int)d.TotalDays} dni {d.Hours} godz.";

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    /// <summary>Stare wyniki znikają — zostają awarie i ostatnie 45 dni sprawdzeń.</summary>
    public async Task CleanupAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var before = DateTime.UtcNow - UptimeRules.BeatsKept;
        await db.UptimeBeats.Where(b => b.At < before).ExecuteDeleteAsync(ct);
        await db.UptimeIncidents.Where(i => i.EndedAt != null && i.EndedAt < DateTime.UtcNow.AddDays(-365)).ExecuteDeleteAsync(ct);
    }

    // ── Statystyki ─────────────────────────────────────────────────────────────

    public sealed record Summary(UptimeMonitor Monitor, List<UptimeBeat> Recent, double? Uptime24h, int? AvgMs24h);

    /// <summary>Lista do panelu: ostatnie sprawdzenia (pasek) i uptime z doby dla każdego monitora.</summary>
    public async Task<List<Summary>> SummariesAsync(int beats = 48, bool publicOnly = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var monitors = await db.UptimeMonitors.AsNoTracking().Where(m => !publicOnly || (m.IsPublic && !m.IsPaused))
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ToListAsync();
        var since = DateTime.UtcNow.AddHours(-24);
        var stats = await db.UptimeBeats.AsNoTracking().Where(b => b.At >= since)
            .GroupBy(b => b.MonitorId)
            .Select(g => new { Id = g.Key, Up = g.Count(b => b.Up), Total = g.Count(), Avg = g.Where(b => b.Up).Average(b => (double?)b.Ms) })
            .ToDictionaryAsync(x => x.Id);
        var result = new List<Summary>();
        foreach (var m in monitors)
        {
            var recent = await db.UptimeBeats.AsNoTracking().Where(b => b.MonitorId == m.Id).OrderByDescending(b => b.At).Take(beats).ToListAsync();
            recent.Reverse();
            stats.TryGetValue(m.Id, out var st);
            result.Add(new Summary(m, recent, st is null ? null : UptimeRules.Percent(st.Up, st.Total), st?.Avg is { } a ? (int)a : null));
        }
        return result;
    }

    public sealed record Detail(UptimeMonitor Monitor, double? Uptime24h, double? Uptime7d, double? Uptime30d, int? AvgMs24h,
        List<(DateTime From, int? AvgMs, bool AnyDown)> Chart, List<UptimeIncident> Incidents, List<UptimeBeat> Failures);

    public async Task<Detail?> DetailAsync(int id, int chartHours = 24)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var m = await db.UptimeMonitors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return null;
        var now = DateTime.UtcNow;
        async Task<double?> Pct(int days)
        {
            var since = now.AddDays(-days);
            var q = db.UptimeBeats.AsNoTracking().Where(b => b.MonitorId == id && b.At >= since);
            return UptimeRules.Percent(await q.CountAsync(b => b.Up), await q.CountAsync());
        }
        var from = now.AddHours(-chartHours);
        var beats = await db.UptimeBeats.AsNoTracking().Where(b => b.MonitorId == id && b.At >= from).OrderBy(b => b.At).ToListAsync();
        var avg = beats.Where(b => b.Up && b.Ms is not null && b.At >= now.AddHours(-24)).Select(b => b.Ms!.Value).DefaultIfEmpty().Average();
        var incidents = await db.UptimeIncidents.AsNoTracking().Where(i => i.MonitorId == id).OrderByDescending(i => i.StartedAt).Take(20).ToListAsync();
        var failures = beats.Where(b => !b.Up).OrderByDescending(b => b.At).Take(10).ToList();
        return new Detail(m, await Pct(1), await Pct(7), await Pct(30), avg > 0 ? (int)avg : null,
            UptimeRules.Buckets(beats, from, now, 72), incidents, failures);
    }

    /// <summary>Dzienne paski na stronę /status: udział udanych sprawdzeń w każdym z ostatnich dni.</summary>
    public async Task<Dictionary<int, List<(DateTime Day, double? Percent)>>> DailyAsync(IEnumerable<int> ids, int days)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var idList = ids.ToList();
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var rows = await db.UptimeBeats.AsNoTracking().Where(b => idList.Contains(b.MonitorId) && b.At >= since)
            .GroupBy(b => new { b.MonitorId, b.At.Date })
            .Select(g => new { g.Key.MonitorId, g.Key.Date, Up = g.Count(b => b.Up), Total = g.Count() })
            .ToListAsync();
        return idList.ToDictionary(id => id, id => Enumerable.Range(0, days).Select(i =>
        {
            var day = since.AddDays(i);
            var r = rows.FirstOrDefault(x => x.MonitorId == id && x.Date == day);
            return (day, r is null ? (double?)null : UptimeRules.Percent(r.Up, r.Total));
        }).ToList());
    }

    // ── Zarządzanie ────────────────────────────────────────────────────────────

    public async Task<int> SaveAsync(UptimeMonitor input)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var m = input.Id == 0 ? new UptimeMonitor() : await db.UptimeMonitors.FirstAsync(x => x.Id == input.Id);
        m.Name = input.Name.Trim();
        // Adres monitora aplikacji trenera ustawia synchronizacja z domeną studia.
        if (m.TenantId is null) m.Url = input.Url.Trim();
        m.Keyword = string.IsNullOrWhiteSpace(input.Keyword) ? null : input.Keyword.Trim();
        m.IntervalSeconds = Math.Clamp(input.IntervalSeconds, 20, 3600);
        m.TimeoutSeconds = Math.Clamp(input.TimeoutSeconds, 2, 60);
        m.RetriesBeforeDown = Math.Clamp(input.RetriesBeforeDown, 1, 10);
        m.IsPublic = input.IsPublic;
        if (input.Id == 0) { m.SortOrder = 50; db.UptimeMonitors.Add(m); }
        await db.SaveChangesAsync();
        return m.Id;
    }

    public async Task SetPausedAsync(int id, bool paused)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var m = await db.UptimeMonitors.FirstAsync(x => x.Id == id);
        m.IsPaused = paused;
        m.State = paused ? UptimeState.Paused : UptimeState.Pending;
        m.StateSince = DateTime.UtcNow;
        m.FailuresInRow = 0;
        if (paused)
            foreach (var i in await db.UptimeIncidents.Where(i => i.MonitorId == id && i.EndedAt == null).ToListAsync()) i.EndedAt = DateTime.UtcNow;
        else m.LastCheckAt = null;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.UptimeBeats.Where(b => b.MonitorId == id).ExecuteDeleteAsync();
        await db.UptimeIncidents.Where(i => i.MonitorId == id).ExecuteDeleteAsync();
        await db.UptimeMonitors.Where(m => m.Id == id && m.TenantId == null).ExecuteDeleteAsync();
    }
}

/// <summary>Co 10 s sprawdza monitory, którym minął interwał; co godzinę synchronizuje monitory studiów i sprząta stare wyniki.</summary>
public class UptimeBackgroundService(IServiceScopeFactory scopes, ILogger<UptimeBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        var lastSync = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var uptime = scope.ServiceProvider.GetRequiredService<UptimeService>();
                if (DateTime.UtcNow - lastSync > TimeSpan.FromHours(1))
                {
                    await uptime.SyncTenantsAsync(stoppingToken);
                    await uptime.CleanupAsync(stoppingToken);
                    lastSync = DateTime.UtcNow;
                }
                await uptime.RunDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Monitor dostępności: iteracja nie powiodła się."); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
