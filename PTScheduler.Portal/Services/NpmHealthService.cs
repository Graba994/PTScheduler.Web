using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Czy domeny trenerów mają działające HTTPS w Nginx Proxy Manager. Sprawdza co 30 minut (i od razu na żądanie),
/// wynik trzyma w pamięci — pasek stanu i pulpit czytają go bez odpytywania NPM.
/// </summary>
public class NpmHealthService(IServiceScopeFactory scopes, ILogger<NpmHealthService> logger) : BackgroundService
{
    public enum DomainState { Ok, NotForced, Expiring, Expired, NoCertificate, NoProxyHost }

    public sealed record TenantDomain(int TenantId, string Slug, string Company, string Domain, DomainState State, int? HostId, int? DaysLeft, DateTime? ExpiresUtc)
    {
        public bool IsProblem => State is DomainState.Expiring or DomainState.Expired or DomainState.NoCertificate or DomainState.NoProxyHost;
    }

    public sealed record Report(DateTime CheckedAt, string? Error, List<NpmProxyHost> Hosts, List<TenantDomain> Tenants)
    {
        public List<TenantDomain> Problems => Tenants.Where(t => t.IsProblem).ToList();
    }

    private readonly SemaphoreSlim _lock = new(1, 1);
    public Report? Current { get; private set; }

    /// <summary>Po każdym sprawdzeniu — pasek stanu odświeża alarm od razu, bez czekania na swój zegar.</summary>
    public event Action? Changed;

    public static string Label(DomainState s, int? daysLeft) => s switch
    {
        DomainState.Ok => daysLeft is { } d ? $"HTTPS · certyfikat jeszcze {d} dni" : "HTTPS",
        DomainState.NotForced => "certyfikat jest, ale HTTPS nie jest wymuszone",
        DomainState.Expiring => $"certyfikat wygasa za {Math.Max(0, daysLeft ?? 0)} dni",
        DomainState.Expired => "certyfikat wygasł",
        DomainState.NoCertificate => "brak HTTPS (bez certyfikatu)",
        _ => "brak wpisu w NPM"
    };

    public static string Tone(DomainState s) => s switch
    {
        DomainState.Ok => "ok",
        DomainState.NotForced or DomainState.Expiring => "warn",
        _ => "danger"
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await RefreshAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Sprawdzenie HTTPS w NPM nie powiodło się."); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), ct); } catch (OperationCanceledException) { return; }
        }
    }

    private Report Publish(Report report)
    {
        Current = report;
        try { Changed?.Invoke(); } catch { /* słuchacz nie może zepsuć sprawdzenia */ }
        return report;
    }

    public async Task<Report> RefreshAsync()
    {
        await _lock.WaitAsync();
        try
        {
            using var scope = scopes.CreateScope();
            var npm = scope.ServiceProvider.GetRequiredService<NpmService>();
            var settings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();

            // Bez skonfigurowanego NPM nie ma czego sprawdzać ani o czym alarmować.
            if (string.IsNullOrWhiteSpace(await settings.GetAsync(SiteSettingsService.Keys.NpmUrl)))
                return Publish(new Report(DateTime.UtcNow, null, [], []));

            var (hosts, error) = await npm.ListProxyHostsDetailedAsync();
            await using var db = await dbFactory.CreateDbContextAsync();
            var tenants = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).ToListAsync();

            var rows = new List<TenantDomain>();
            if (error is null)
            {
                foreach (var t in tenants)
                {
                    var domain = Normalize(t.Domain);
                    if (!IsPublicDomain(domain)) continue;
                    var host = hosts.FirstOrDefault(h => string.Equals(h.Domain, domain, StringComparison.OrdinalIgnoreCase));
                    var state = host is null ? DomainState.NoProxyHost : host.HttpsState switch
                    {
                        NpmHttpsState.Ok => DomainState.Ok,
                        NpmHttpsState.NotForced => DomainState.NotForced,
                        NpmHttpsState.Expiring => DomainState.Expiring,
                        NpmHttpsState.Expired => DomainState.Expired,
                        _ => DomainState.NoCertificate
                    };
                    rows.Add(new TenantDomain(t.Id, t.Slug, t.CompanyName, domain, state, host?.Id, host?.DaysLeft, host?.CertExpiresUtc));
                }
            }
            return Publish(new Report(DateTime.UtcNow, error, hosts, rows.OrderBy(r => r.IsProblem ? 0 : 1).ThenBy(r => r.Slug).ToList()));
        }
        finally { _lock.Release(); }
    }

    public TenantDomain? ForTenant(int tenantId) => Current?.Tenants.FirstOrDefault(t => t.TenantId == tenantId);

    private static string Normalize(string domain)
    {
        var d = (domain ?? "").Trim();
        if (Uri.TryCreate(d, UriKind.Absolute, out var u)) d = u.Host;
        return d.TrimEnd('/');
    }

    /// <summary>Adresy IP, localhost i domeny z portem to instalacje testowe — HTTPS przez NPM ich nie dotyczy.</summary>
    private static bool IsPublicDomain(string d) =>
        d.Contains('.') && !d.Contains(':') && !System.Net.IPAddress.TryParse(d, out _)
        && !d.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !d.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);
}
