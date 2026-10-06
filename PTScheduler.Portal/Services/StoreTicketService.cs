using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Zgłoszenia ze sklepu usług: usługi z realizacją (szkolenie, konfiguracja płatności, pakiet wsparcia)
/// trafiają do Portalu jako „do zrobienia” (/panel/orders), a administrator dostaje e-mail i — po
/// włączeniu — SMS. Tu też dopisujemy kwotę do rachunku trenera, gdy nie zapłacił online.
/// </summary>
public class StoreTicketService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    EmailService email,
    CreditService credits,
    IConfiguration config,
    ILogger<StoreTicketService> logger)
{
    /// <summary>Usługa, którą ktoś z zespołu musi wykonać (a nie limit, który włącza się sam).</summary>
    public static bool IsTicket(ServiceItem item) => item.FulfillmentType == "manual";

    /// <summary>Dopisuje zamówienie do rachunku trenera jako jednorazową opłatę (widoczną w ofercie i PDF).</summary>
    public static void ChargeToBill(PortalDbContext db, ServiceOrder order, ServiceItem item)
    {
        if (order.ChargedToBillAt is not null || order.PaidAt is not null || order.Price <= 0) return;
        db.TenantOfferItems.Add(new TenantOfferItem
        {
            TenantId = order.TenantId,
            Kind = OfferItemKind.Charge,
            Name = item.Name,
            Description = item.Description,
            ServiceItemId = item.Id,
            Quantity = 1,
            UnitPrice = order.Price,
            Billing = OfferBilling.OneTime,
            Effect = OfferEffect.None, // efekt (np. SMS-y) dopisał już sklep
            InternalNote = $"Zamówienie #{order.Id} ze sklepu — do najbliższego rachunku",
            CreatedBy = "sklep"
        });
        order.ChargedToBillAt = DateTime.UtcNow;
    }

    /// <summary>E-mail i SMS do administratora o nowych zgłoszeniach (każde osobno, żeby było widać, co zrobić).</summary>
    public async Task NotifyNewTicketsAsync(IReadOnlyCollection<int> orderIds)
    {
        if (orderIds.Count == 0) return;
        await using var db = dbFactory.CreateDbContext();
        var orders = await db.ServiceOrders.AsNoTracking().Include(o => o.Tenant).Include(o => o.ServiceItem)
            .Where(o => orderIds.Contains(o.Id)).ToListAsync();
        if (orders.Count == 0) return;

        var s = await settings.GetAllAsync(SiteSettingsService.Keys.AdminNotificationEmail, SiteSettingsService.Keys.NotifyTicketsEmail,
            SiteSettingsService.Keys.AdminNotificationPhone, SiteSettingsService.Keys.NotifyTicketsSms);
        var adminEmail = s[SiteSettingsService.Keys.AdminNotificationEmail];
        var emailOn = !string.IsNullOrWhiteSpace(adminEmail) && s[SiteSettingsService.Keys.NotifyTicketsEmail] != "false";
        var phone = s[SiteSettingsService.Keys.AdminNotificationPhone];
        var smsOn = !string.IsNullOrWhiteSpace(phone) && s[SiteSettingsService.Keys.NotifyTicketsSms] == "true";
        var panelUrl = $"{(config.GetValue<string>("Portal:PublicUrl") ?? "").TrimEnd('/')}/panel/orders";

        foreach (var o in orders)
        {
            var who = o.Tenant.CompanyName ?? o.Tenant.OwnerName ?? o.Tenant.Slug;
            var what = o.ServiceItem.Name;
            if (emailOn)
            {
                try
                {
                    var html = email.NewServiceOrderAdminEmailBody(o.Tenant.OwnerName ?? "—", o.Tenant.CompanyName ?? "—", what, o.Price, o.Id, o.Notes);
                    await email.SendAsync(adminEmail, $"Nowe zgłoszenie do zrobienia: {what} — {who}", html);
                }
                catch (Exception ex) { logger.LogError(ex, "E-mail o zgłoszeniu #{Id} nie wyszedł.", o.Id); }
            }
            if (smsOn)
            {
                var (ok, error) = await credits.SendPlatformSmsAsync(phone,
                    $"PTScheduler: nowe zgloszenie #{o.Id} - {what} ({who}). {panelUrl}");
                if (!ok) logger.LogWarning("SMS o zgłoszeniu #{Id} nie wyszedł: {Error}", o.Id, error);
            }
        }
    }
}
