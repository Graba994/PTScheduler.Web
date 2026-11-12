using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using Stripe;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Miesięczne dodatki do abonamentu trenera (SMS-y, transfer i przestrzeń wideo).
/// Z subskrypcją Stripe dodatek staje się jej pozycją — Stripe dolicza go co
/// miesiąc razem z planem, a rezygnacja usuwa pozycję z proporcjonalnym
/// rozliczeniem. Bez Stripe dodatek aktywuje opłacone zamówienie w sklepie.
/// </summary>
public class AddonService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    TenantService tenants,
    StoreTicketService tickets,
    ILogger<AddonService> logger)
{
    /// <summary>
    /// Pozycja co miesiąc, dopisywana do abonamentu jednym kliknięciem: limity (SMS-y, wideo)
    /// albo usługa z realizacją (np. pakiet wsparcia — wtedy dodatkowo zgłoszenie dla zespołu).
    /// </summary>
    public static bool IsMonthlyAddon(ServiceItem item) =>
        item.PriceType == "monthly"
        && (item.FulfillmentType == "manual"
            || (item.FulfillmentType is "credit_cdn_bandwidth" or "credit_cdn_storage" or "credit_sms" && item.CreditAmount > 0));

    /// <summary>
    /// Dodaje dodatek od razu: z abonamentem płaconym kartą (Stripe) jako pozycję subskrypcji,
    /// bez karty — do miesięcznego rachunku (oferta trenera). Limity rosną natychmiast.
    /// </summary>
    /// <returns>Mode: „card” albo „invoice”.</returns>
    public async Task<(bool Ok, string? Error, string? Mode)> AddAsync(int tenantId, int serviceItemId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        var item = await db.ServiceItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == serviceItemId && i.IsActive);
        if (tenant is null || item is null || !IsMonthlyAddon(item)) return (false, "Nie ma takiego dodatku.", null);
        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Destroyed || tenant.BillingStatus == "past_due")
            return (false, "Najpierw ureguluj abonament — potem dodasz dodatki.", null);

        string mode;
        if (await CanAutoBillAsync(tenant, item))
        {
            var (ok, error) = await AddToSubscriptionAsync(tenantId, serviceItemId);
            if (!ok) return (false, error, null);
            mode = "card";
        }
        else
        {
            await AddToInvoiceAsync(tenantId, item);
            mode = "invoice";
        }

        // Usługa z realizacją (np. pakiet wsparcia): zespół dostaje zgłoszenie do zrobienia.
        if (StoreTicketService.IsTicket(item))
        {
            await using var tdb = dbFactory.CreateDbContext();
            var price = await tdb.TenantServicePrices.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.ServiceItemId == item.Id)
                .Select(p => (decimal?)p.CustomPrice).FirstOrDefaultAsync() ?? item.DefaultPrice;
            var order = new ServiceOrder
            {
                TenantId = tenantId, ServiceItemId = item.Id, Price = price,
                Notes = "Dodatek miesięczny — rozliczany razem z abonamentem.",
                Status = ServiceOrderStatus.Pending, ChargedToBillAt = DateTime.UtcNow
            };
            tdb.ServiceOrders.Add(order);
            await tdb.SaveChangesAsync();
            await tickets.NotifyNewTicketsAsync([order.Id]);
        }
        return (true, null, mode);
    }

    /// <summary>Bez karty: dodatek aktywny od razu, a kwota dolicza się do miesięcznego rachunku.</summary>
    private async Task AddToInvoiceAsync(int tenantId, ServiceItem item)
    {
        await using var db = dbFactory.CreateDbContext();
        var existing = await db.TenantAddons.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.ServiceItemId == item.Id
            && a.Status == TenantAddonStatus.Active && a.StripeSubscriptionItemId == null);
        if (existing is not null) existing.Quantity++;
        else db.TenantAddons.Add(new TenantAddon { TenantId = tenantId, ServiceItemId = item.Id });
        db.TenantEvents.Add(new TenantEvent { TenantId = tenantId, EventType = TenantEventTypes.AddonChanged, Detail = $"Dodatek miesięczny do rachunku: {item.Name}" });
        await db.SaveChangesAsync();
        await tenants.PushEntitlementsAsync(tenantId);
    }

    /// <summary>
    /// Co trener ma ponad plan: GB z aktywnych dodatków i pozycji oferty, SMS-y miesięcznie z oferty
    /// oraz czy ma kredyty SMS (wtedy przypomnienia SMS działają nawet w planie bez SMS).
    /// </summary>
    public static async Task<ExtraLimits> ExtraLimitsAsync(PortalDbContext db, int tenantId)
    {
        var rows = await db.TenantAddons.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Status == TenantAddonStatus.Active)
            .Select(a => new { a.Quantity, a.ServiceItem!.FulfillmentType, a.ServiceItem.CreditAmount })
            .ToListAsync();
        var now = DateTime.UtcNow;
        var offer = (await db.TenantOfferItems.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.CancelledAt == null && o.Effect != OfferEffect.None)
                .ToListAsync())
            .Where(o => o.IsActiveAt(now))
            .ToList();
        var smsCredits = await db.TenantCredits.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CreditType == "sms")
            .Select(c => c.Balance).FirstOrDefaultAsync();

        return new ExtraLimits(
            StorageGb: rows.Where(r => r.FulfillmentType == "credit_cdn_storage").Sum(r => r.Quantity * r.CreditAmount)
                       + offer.Where(o => o.Effect == OfferEffect.VideoStorageGb).Sum(o => o.TotalEffect),
            BandwidthGb: rows.Where(r => r.FulfillmentType == "credit_cdn_bandwidth").Sum(r => r.Quantity * r.CreditAmount)
                         + offer.Where(o => o.Effect == OfferEffect.VideoBandwidthGb).Sum(o => o.TotalEffect),
            SmsPerMonth: rows.Where(r => r.FulfillmentType == "credit_sms").Sum(r => r.Quantity * r.CreditAmount)
                         + offer.Where(o => o.Effect == OfferEffect.Sms && o.Billing != OfferBilling.OneTime).Sum(o => o.TotalEffect),
            HasSmsCredits: smsCredits > 0);
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

/// <summary>Limity ponad plan (dodatki i oferta) doliczane do uprawnień instancji.</summary>
public sealed record ExtraLimits(int StorageGb, int BandwidthGb, int SmsPerMonth, bool HasSmsCredits)
{
    public static readonly ExtraLimits None = new(0, 0, 0, false);
}
