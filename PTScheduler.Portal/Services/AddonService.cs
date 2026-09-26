using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using Stripe;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Miesięczne dodatki do abonamentu trenera (transfer i przestrzeń wideo).
/// Z subskrypcją Stripe dodatek staje się jej pozycją — Stripe dolicza go co
/// miesiąc razem z planem, a rezygnacja usuwa pozycję z proporcjonalnym
/// rozliczeniem. Bez Stripe dodatek aktywuje opłacone zamówienie w sklepie.
/// </summary>
public class AddonService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    TenantService tenants,
    ILogger<AddonService> logger)
{
    public static bool IsMonthlyAddon(ServiceItem item) =>
        item.PriceType == "monthly"
        && item.FulfillmentType is "credit_cdn_bandwidth" or "credit_cdn_storage"
        && item.CreditAmount > 0;

    /// <summary>Dodatkowe GB z aktywnych dodatków — doliczane do limitów planu.</summary>
    public static async Task<(int StorageGb, int BandwidthGb)> ExtraLimitsAsync(PortalDbContext db, int tenantId)
    {
        var rows = await db.TenantAddons.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Status == TenantAddonStatus.Active)
            .Select(a => new { a.Quantity, a.ServiceItem!.FulfillmentType, a.ServiceItem.CreditAmount })
            .ToListAsync();
        return (
            rows.Where(r => r.FulfillmentType == "credit_cdn_storage").Sum(r => r.Quantity * r.CreditAmount),
            rows.Where(r => r.FulfillmentType == "credit_cdn_bandwidth").Sum(r => r.Quantity * r.CreditAmount));
    }

    public async Task<List<TenantAddon>> GetActiveAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.TenantAddons.AsNoTracking().Include(a => a.ServiceItem)
            .Where(a => a.TenantId == tenantId && a.Status == TenantAddonStatus.Active)
            .OrderBy(a => a.StartedAt)
            .ToListAsync();
    }

    /// <summary>Czy dodatek można dopisać do subskrypcji Stripe tego trenera (bez osobnej płatności).</summary>
    public async Task<bool> CanAutoBillAsync(Tenant tenant, ServiceItem item) =>
        IsMonthlyAddon(item)
        && !string.IsNullOrWhiteSpace(item.StripePriceId)
        && !string.IsNullOrWhiteSpace(tenant.StripeSubscriptionId)
        && tenant.BillingStatus is "active" or "trialing"
        && !string.IsNullOrWhiteSpace(await settings.GetAsync(SiteSettingsService.Keys.StripeSecretKey));

    /// <summary>Dopisuje dodatek do subskrypcji Stripe (albo zwiększa jego ilość).</summary>
    public async Task<(bool Ok, string? Error)> AddToSubscriptionAsync(int tenantId, int serviceItemId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        var item = await db.ServiceItems.FirstOrDefaultAsync(i => i.Id == serviceItemId && i.IsActive);
        if (tenant is null || item is null) return (false, "Nie ma takiego dodatku.");
        if (!await CanAutoBillAsync(tenant, item)) return (false, "Ten dodatek trzeba zamówić w sklepie (abonament nie jest opłacany kartą).");

        StripeConfiguration.ApiKey = await settings.GetAsync(SiteSettingsService.Keys.StripeSecretKey);
        var service = new SubscriptionItemService();
        var existing = await db.TenantAddons.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.ServiceItemId == serviceItemId
            && a.Status == TenantAddonStatus.Active && a.StripeSubscriptionItemId != null);
        try
        {
            if (existing is not null)
            {
                await service.UpdateAsync(existing.StripeSubscriptionItemId, new SubscriptionItemUpdateOptions
                {
                    Quantity = existing.Quantity + 1,
                    ProrationBehavior = "create_prorations"
                });
                existing.Quantity++;
            }
            else
            {
                var created = await service.CreateAsync(new SubscriptionItemCreateOptions
                {
                    Subscription = tenant.StripeSubscriptionId,
                    Price = item.StripePriceId,
                    Quantity = 1,
                    ProrationBehavior = "create_prorations",
                    Metadata = new Dictionary<string, string> { ["tenantId"] = tenant.Id.ToString(), ["serviceItemId"] = item.Id.ToString() }
                });
                db.TenantAddons.Add(new TenantAddon { TenantId = tenantId, ServiceItemId = item.Id, StripeSubscriptionItemId = created.Id });
            }
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe: nie dodano dodatku {Item} do subskrypcji trenera {Tenant}.", item.Id, tenantId);
            return (false, "Płatności nie przyjęły zmiany abonamentu. Spróbuj ponownie za chwilę.");
        }

        db.TenantEvents.Add(new TenantEvent { TenantId = tenantId, EventType = TenantEventTypes.AddonChanged, Detail = $"Dodatek miesięczny: {item.Name}" });
        await db.SaveChangesAsync();
        await tenants.PushEntitlementsAsync(tenantId);
        return (true, null);
    }

    /// <summary>Aktywuje dodatek po opłaconym zamówieniu w sklepie (instancja bez Stripe).</summary>
    public static void ActivateFromOrder(PortalDbContext db, int tenantId, ServiceItem item, int orderId)
    {
        db.TenantAddons.Add(new TenantAddon { TenantId = tenantId, ServiceItemId = item.Id });
        db.TenantEvents.Add(new TenantEvent { TenantId = tenantId, EventType = TenantEventTypes.AddonChanged, Detail = $"Dodatek miesięczny z zamówienia #{orderId}: {item.Name}" });
    }

    /// <summary>Rezygnacja: zmniejsza ilość albo usuwa pozycję z subskrypcji; limity spadają od razu.</summary>
    public async Task<(bool Ok, string? Error)> CancelAsync(int tenantId, int addonId)
    {
        await using var db = dbFactory.CreateDbContext();
        var addon = await db.TenantAddons.Include(a => a.ServiceItem)
            .FirstOrDefaultAsync(a => a.Id == addonId && a.TenantId == tenantId && a.Status == TenantAddonStatus.Active);
        if (addon is null) return (false, "Ten dodatek nie jest aktywny.");

        if (addon.StripeSubscriptionItemId is not null)
        {
            StripeConfiguration.ApiKey = await settings.GetAsync(SiteSettingsService.Keys.StripeSecretKey);
            var service = new SubscriptionItemService();
            try
            {
                if (addon.Quantity > 1)
                    await service.UpdateAsync(addon.StripeSubscriptionItemId, new SubscriptionItemUpdateOptions { Quantity = addon.Quantity - 1, ProrationBehavior = "create_prorations" });
                else
                    await service.DeleteAsync(addon.StripeSubscriptionItemId, new SubscriptionItemDeleteOptions { ProrationBehavior = "create_prorations" });
            }
            catch (StripeException ex) when (ex.StripeError?.Code != "resource_missing")
            {
                logger.LogWarning(ex, "Stripe: nie usunięto dodatku {Addon}.", addonId);
                return (false, "Płatności nie przyjęły zmiany abonamentu. Spróbuj ponownie za chwilę.");
            }
        }

        if (addon.Quantity > 1) addon.Quantity--;
        else
        {
            addon.Status = TenantAddonStatus.Cancelled;
            addon.CancelledAt = DateTime.UtcNow;
        }
        db.TenantEvents.Add(new TenantEvent { TenantId = tenantId, EventType = TenantEventTypes.AddonChanged, Detail = $"Rezygnacja z dodatku: {addon.ServiceItem?.Name}" });
        await db.SaveChangesAsync();
        await tenants.PushEntitlementsAsync(tenantId);
        return (true, null);
    }
}
