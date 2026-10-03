using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Własna domena trenera (np. annafit.pl) z kreatora: co 10 minut sprawdza, czy DNS wskazuje już nasz
/// serwer. Gdy tak — rejestruje domenę w Nginx Proxy Manager z certyfikatem Let's Encrypt (obok adresu
/// w domenie platformy) i wysyła trenerowi e-mail. Po 7 dniach bez DNS przestaje czekać i daje znać, co ustawić.
/// </summary>
public class CustomDomainService(IServiceScopeFactory scopes, ILogger<CustomDomainService> logger) : BackgroundService
{
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(7);
    private readonly SemaphoreSlim _lock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await CheckAllAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Sprawdzanie własnych domen nie powiodło się."); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), ct); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task CheckAllAsync()
    {
        using var scope = scopes.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = await db.Tenants.AsNoTracking()
            .Where(t => t.CustomDomainStatus == "waiting" && t.Status == TenantStatus.Active && t.CustomDomain != null)
            .Select(t => t.Id).ToListAsync();
        foreach (var id in ids) await CheckTenantAsync(id);
    }

    /// <summary>Sprawdza jedną domenę (także na żądanie z karty trenera). Zwraca opis stanu.</summary>
    public async Task<string> CheckTenantAsync(int tenantId)
    {
        await _lock.WaitAsync();
        try
        {
            using var scope = scopes.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();
            var npm = scope.ServiceProvider.GetRequiredService<NpmService>();
            var settings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
            var email = scope.ServiceProvider.GetRequiredService<EmailService>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            await using var db = await dbFactory.CreateDbContextAsync();
            var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
            if (t?.CustomDomain is not { Length: > 0 } domain) return "Brak własnej domeny.";
            if (t.CustomDomainStatus == "active") return $"{domain} działa.";

            var expected = await ResolveAsync(t.Domain);
            var mainDomain = await settings.GetAsync(SiteSettingsService.Keys.MainDomain);
            if (expected.Count == 0 && !string.IsNullOrWhiteSpace(mainDomain)) expected = await ResolveAsync(mainDomain);
            var actual = await ResolveAsync(domain);

            var (first, _) = TenantSetupPayload.SplitName(t.OwnerName);
            if (actual.Count > 0 && (expected.Count == 0 || actual.Overlaps(expected)))
            {
                if (string.IsNullOrWhiteSpace(await settings.GetAsync(SiteSettingsService.Keys.NpmUrl)))
                    return $"{domain} wskazuje serwer, ale NPM nie jest skonfigurowany — dodaj domenę ręcznie.";

                var forwardHost = config.GetValue<string>("Portal:ForwardHost") ?? "192.168.0.220";
                var (ok, msg) = await npm.RegisterProxyHostAsync(domain, forwardHost, t.Port, ssl: true);
                if (ok && msg.Contains("z HTTPS"))
                {
                    var www = $"www.{domain}";
                    if ((await ResolveAsync(www)).Overlaps(actual))
                        try { await npm.RegisterProxyHostAsync(www, forwardHost, t.Port, ssl: true); } catch (Exception ex) { logger.LogDebug(ex, "www.{Domain} nie dodane.", domain); }
                    t.CustomDomainStatus = "active";
                    db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.DomainChanged, Detail = $"Własna domena {domain} działa (HTTPS)" });
                    await db.SaveChangesAsync();
                    _ = email.SendAsync(t.OwnerEmail, $"{domain} działa — PTScheduler", email.CustomDomainActiveEmailBody(first ?? t.OwnerName, domain));
                    logger.LogInformation("Własna domena {Domain} tenanta {Slug} działa.", domain, t.Slug);
                    return $"{domain} działa — certyfikat wystawiony.";
                }
                logger.LogInformation("Własna domena {Domain}: DNS wskazuje serwer, ale NPM: {Msg}", domain, msg);
                if (!Expired(t)) return $"DNS wskazuje serwer, ale certyfikat jeszcze nie wyszedł: {msg}";
            }

            if (Expired(t))
            {
                t.CustomDomainStatus = "failed";
                await db.SaveChangesAsync();
                _ = email.SendAsync(t.OwnerEmail, $"{domain} — nie widzimy ustawień DNS",
                    email.CustomDomainFailedEmailBody(first ?? t.OwnerName, domain, t.Domain, expected.FirstOrDefault()?.ToString()));
                return $"{domain}: DNS nie wskazał serwera w 7 dni — trener dostał instrukcję.";
            }
            return actual.Count == 0
                ? $"{domain} jeszcze nie ma rekordów DNS."
                : $"{domain} wskazuje {string.Join(", ", actual)}, a serwer to {(expected.Count > 0 ? string.Join(", ", expected) : "?")}.";
        }
        finally { _lock.Release(); }
    }

    private static bool Expired(Tenant t) => t.CustomDomainSince is { } since && DateTime.UtcNow - since > GiveUpAfter;

    private static async Task<HashSet<IPAddress>> ResolveAsync(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return [];
        try
        {
            var ips = await Dns.GetHostAddressesAsync(host.Trim()).WaitAsync(TimeSpan.FromSeconds(5));
            return ips.Where(i => i.AddressFamily == AddressFamily.InterNetwork).ToHashSet();
        }
        catch (Exception) { return []; }
    }
}
