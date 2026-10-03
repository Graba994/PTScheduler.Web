using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Co godzinę: odczytuje z aplikacji trenerów liczbę klientów i treningów (aktywacja = 3 klientów
/// i pierwszy trening), zapisuje pierwszą płatność za abonament (lejek + nagroda za polecenie)
/// i wysyła e-maile powitalne w dniu 1, 3 i 7 po uruchomieniu aplikacji.
/// </summary>
public class GrowthService(IServiceScopeFactory scopes, IConfiguration config, ILogger<GrowthService> logger) : BackgroundService
{
    public const int ActivationClients = 3;
    private static readonly TimeSpan MetricsEvery = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await RunAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Obieg wzrostu (aktywacja, płatności, e-maile) nie powiódł się."); }
            try { await Task.Delay(TimeSpan.FromHours(1), ct); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        await RefreshMetricsAsync(sp, ct);
        await DetectFirstPaymentsAsync(sp);
        await SendOnboardingAsync(sp);
    }

    private async Task RefreshMetricsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var dbFactory = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var stale = DateTime.UtcNow - MetricsEvery;
        var tenants = await db.Tenants.Where(t => t.Status == TenantStatus.Active && (t.MetricsAt == null || t.MetricsAt < stale)).ToListAsync(ct);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        foreach (var t in tenants)
        {
            try
            {
                var baseUrl = await TenantEndpoint.BaseUrlAsync(config, t);
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/internal/metrics");
                if (TenantSecrets.For(t, config) is { Length: > 0 } secret) req.Headers.Add("X-Internal-Secret", secret);
                using var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) continue;
                var m = await resp.Content.ReadFromJsonAsync<Metrics>(ct);
                if (m is null) continue;
                t.MetricsClients = m.Clients;
                t.MetricsSessions = m.SessionsTotal;
                t.MetricsAt = DateTime.UtcNow;
                if (t.ActivatedAt is null && m.Clients >= ActivationClients && m.SessionsTotal >= 1)
                {
                    t.ActivatedAt = DateTime.UtcNow;
                    db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.Activated, Detail = $"{m.Clients} klientów, {m.SessionsTotal} treningów" });
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                logger.LogDebug(ex, "Metryki {Slug} niedostępne.", t.Slug);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private sealed record Metrics(int Clients, int SessionsTotal);

    /// <summary>Prawdziwa płatność: opłacona faktura Stripe z kwotą albo opłacony rachunek (bez płatności weryfikacyjnej).</summary>
    private static async Task DetectFirstPaymentsAsync(IServiceProvider sp)
    {
        var dbFactory = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var referrals = sp.GetRequiredService<ReferralService>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var paid = await db.PaymentRecords.AsNoTracking()
            .Where(p => p.Status == PaymentRecordStatus.Paid && p.Amount > 0
                && ((p.StripeInvoiceId != null) || (p.Description != null && p.Description.StartsWith("Rachunek "))))
            .Where(p => db.Tenants.Any(t => t.Id == p.TenantId && t.FirstPaidAt == null))
            .GroupBy(p => p.TenantId).Select(g => new { TenantId = g.Key, At = g.Min(p => p.CreatedAt) })
            .ToListAsync();
        foreach (var p in paid)
        {
            var t = await db.Tenants.FindAsync(p.TenantId);
            if (t is null || t.FirstPaidAt is not null) continue;
            t.FirstPaidAt = p.At;
            await db.SaveChangesAsync();
            if (t.ReferredByTenantId is not null && t.ReferralRewardedAt is null) await referrals.RewardAsync(t.Id);
        }
    }

    private async Task SendOnboardingAsync(IServiceProvider sp)
    {
        var settings = sp.GetRequiredService<SiteSettingsService>();
        if (await settings.GetAsync(SiteSettingsService.Keys.OnboardingEmails) == "false") return;
        var dbFactory = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var email = sp.GetRequiredService<EmailService>();
        var tenantSvc = sp.GetRequiredService<TenantService>();
        var referrals = sp.GetRequiredService<ReferralService>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var window = now.AddDays(-14);
        var tenants = await db.Tenants.Where(t => t.Status == TenantStatus.Active && t.OnboardingStage < 3
            && t.ProvisionedAt != null && t.ProvisionedAt > window).ToListAsync();
        foreach (var t in tenants)
        {
            var age = now - t.ProvisionedAt!.Value;
            var due = age >= TimeSpan.FromDays(7) ? 3 : age >= TimeSpan.FromDays(3) ? 2 : age >= TimeSpan.FromHours(20) ? 1 : 0;
            if (due <= t.OnboardingStage) continue;
            // Zaległe e-maile pomijamy — wysyłamy tylko ten, który wypada teraz (bez serii naraz).
            var (first, _) = TenantSetupPayload.SplitName(t.OwnerName);
            var appUrl = await tenantSvc.PublicUrlAsync(t);
            var (subject, body) = email.OnboardingEmail(due, first ?? t.OwnerName, t.CompanyName, appUrl,
                t.MetricsClients, t.MetricsSessions, referrals.LinkFor(t.Slug));
            await email.SendAsync(t.OwnerEmail, subject, body);
            t.OnboardingStage = due;
            db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.OnboardingEmail, Detail = $"E-mail powitalny {due}/3: {subject}" });
            await db.SaveChangesAsync();
        }
    }
}
