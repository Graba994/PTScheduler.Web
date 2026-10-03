using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Pilnuje dat w ofertach trenerów: gdy pozycja z efektem zaczyna się albo kończy, odświeża uprawnienia
/// instancji (limity SMS i wideo), a jednorazowe SMS-y z przyszłą datą dopisuje w dniu rozpoczęcia.
/// </summary>
public sealed class OfferSyncService(IServiceScopeFactory scopes, ILogger<OfferSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastCheck = DateTime.UtcNow - Interval;
        await Task.Delay(TimeSpan.FromMinutes(1), ct);
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            try { await SyncAsync(lastCheck, now, ct); lastCheck = now; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Synchronizacja ofert trenerów nie powiodła się."); }
            await Task.Delay(Interval, ct);
        }
    }

    private async Task SyncAsync(DateTime since, DateTime now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>().CreateDbContextAsync(ct);
        var offers = scope.ServiceProvider.GetRequiredService<OfferService>();
        var tenants = scope.ServiceProvider.GetRequiredService<TenantService>();

        var dueSms = await db.TenantOfferItems.AsNoTracking()
            .Where(i => i.Effect == OfferEffect.Sms && i.Billing == OfferBilling.OneTime && i.EffectAppliedAt == null
                        && i.CancelledAt == null && i.StartsAt <= now)
            .Select(i => i.Id).ToListAsync(ct);
        foreach (var id in dueSms) await offers.ApplyDueEffectsAsync(id);

        // Pozycje, które w tym oknie zaczęły działać albo wygasły — limity instancji trzeba przeliczyć.
        var changed = await db.TenantOfferItems.AsNoTracking()
            .Where(i => i.Effect != OfferEffect.None
                        && ((i.StartsAt > since && i.StartsAt <= now) || (i.EndsAt > since && i.EndsAt <= now)))
            .Select(i => i.TenantId).Distinct().ToListAsync(ct);
        foreach (var tenantId in changed.Union(await TenantsOfAsync(db, dueSms, ct)))
            await tenants.PushEntitlementsAsync(tenantId);
    }

    private static Task<List<int>> TenantsOfAsync(PortalDbContext db, List<int> itemIds, CancellationToken ct) =>
        itemIds.Count == 0 ? Task.FromResult(new List<int>())
            : db.TenantOfferItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => i.TenantId).Distinct().ToListAsync(ct);
}
