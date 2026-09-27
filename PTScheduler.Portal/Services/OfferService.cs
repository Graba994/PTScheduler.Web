using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>Dodatek miesięczny w zestawieniu oferty (z ceną dla tego trenera).</summary>
public sealed record OfferAddonLine(string Name, int Quantity, decimal UnitPrice, bool ViaCard, DateTime Since)
{
    public decimal Total => UnitPrice * Quantity;
}

/// <summary>Wszystko, co trener ma i za co płaci — do strony oferty i PDF-u.</summary>
public sealed class OfferSummary
{
    public required Tenant Tenant { get; init; }
    public Plan? Plan { get; init; }
    /// <summary>Kwota abonamentu i cykl („monthly” / „yearly”).</summary>
    public decimal PlanAmount { get; init; }
    public string PlanCycle { get; init; } = OfferBilling.Monthly;
    public List<OfferAddonLine> Addons { get; init; } = [];
    public List<ServiceOrder> Orders { get; init; } = [];
    public List<TenantOfferItem> Gifts { get; init; } = [];
    public List<TenantOfferItem> Charges { get; init; } = [];
    public required SmsStatus Sms { get; init; }
    public int ExtraStorageGb { get; init; }
    public int ExtraBandwidthGb { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    private IEnumerable<TenantOfferItem> ActiveCharges(string billing) =>
        Charges.Where(c => c.Billing == billing && c.IsActiveAt(GeneratedAt));

    /// <summary>Co miesiąc: abonament miesięczny + dodatki + aktywne opłaty miesięczne.</summary>
    public decimal MonthlyTotal =>
        (PlanCycle == OfferBilling.Monthly ? PlanAmount : 0) + Addons.Sum(a => a.Total) + ActiveCharges(OfferBilling.Monthly).Sum(c => c.Total);

    /// <summary>Co rok: abonament roczny + aktywne opłaty roczne.</summary>
    public decimal YearlyTotal =>
        (PlanCycle == OfferBilling.Yearly ? PlanAmount : 0) + ActiveCharges(OfferBilling.Yearly).Sum(c => c.Total);

    /// <summary>Jednorazowe opłaty jeszcze niedoliczone do rachunku.</summary>
    public List<TenantOfferItem> OneTimeDue =>
        Charges.Where(c => c.Billing == OfferBilling.OneTime && c.CancelledAt is null && c.SettledAt is null).ToList();

    public List<TenantOfferItem> ActiveGifts => Gifts.Where(g => g.IsActiveAt(GeneratedAt) || (g.Billing == OfferBilling.OneTime && g.CancelledAt is null)).ToList();

    /// <summary>Wartość gratisów: miesięcznych (co miesiąc) i jednorazowych.</summary>
    public decimal GiftValueMonthly => Gifts.Where(g => g.Billing == OfferBilling.Monthly && g.IsActiveAt(GeneratedAt)).Sum(g => g.Total);
    public decimal GiftValueOneTime => Gifts.Where(g => g.Billing == OfferBilling.OneTime && g.CancelledAt is null).Sum(g => g.Total);
}

/// <summary>Usługa z katalogu podpowiadana w formularzu oferty.</summary>
public sealed record OfferCatalogOption(int Id, string Name, string? Description, decimal Price, string Billing, string Effect, int EffectAmount);

/// <summary>Dane nowej pozycji oferty z formularza w Portalu.</summary>
public sealed class OfferItemInput
{
    public string Kind { get; set; } = OfferItemKind.Gift;
    public int? ServiceItemId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? InternalNote { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public string Billing { get; set; } = OfferBilling.OneTime;
    public string Effect { get; set; } = OfferEffect.None;
    public int EffectAmount { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}

/// <summary>
/// „Oferta trenera”: abonament, to co trener zamówił sam, gratisy i dodatkowe opłaty od administratora.
/// Pozycje z efektem od razu zmieniają aplikację trenera (SMS-y, limity wideo).
/// </summary>
public class OfferService(
    IDbContextFactory<PortalDbContext> dbFactory,
    CreditService credits,
    TenantService tenants,
    ILogger<OfferService> logger)
{
    public async Task<OfferSummary?> GetAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return null;

        var subscription = await db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired)
            .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
        var cycle = subscription?.BillingCycle is "yearly" or "annual" or "year" ? OfferBilling.Yearly : OfferBilling.Monthly;
        var planAmount = subscription?.Amount
                         ?? (cycle == OfferBilling.Yearly ? tenant.Plan?.YearlyPrice ?? (tenant.Plan?.MonthlyPrice ?? 0) * 12 : tenant.Plan?.MonthlyPrice ?? 0);

        var customPrices = await db.TenantServicePrices.AsNoTracking()
            .Where(p => p.TenantId == tenantId).ToDictionaryAsync(p => p.ServiceItemId, p => p.CustomPrice);
        var addons = await db.TenantAddons.AsNoTracking().Include(a => a.ServiceItem)
            .Where(a => a.TenantId == tenantId && a.Status == TenantAddonStatus.Active)
            .OrderBy(a => a.StartedAt).ToListAsync();

        var orders = await db.ServiceOrders.AsNoTracking().Include(o => o.ServiceItem)
            .Where(o => o.TenantId == tenantId)
            .OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync();

        var items = await db.TenantOfferItems.AsNoTracking().Include(i => i.ServiceItem)
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CancelledAt == null).ThenByDescending(i => i.CreatedAt)
            .ToListAsync();

        var extra = await AddonService.ExtraLimitsAsync(db, tenantId);

        return new OfferSummary
        {
            Tenant = tenant,
            Plan = tenant.Plan,
            PlanAmount = planAmount,
            PlanCycle = cycle,
            Addons = addons.Select(a => new OfferAddonLine(
                a.ServiceItem?.Name ?? "Dodatek",
                a.Quantity,
                customPrices.TryGetValue(a.ServiceItemId, out var custom) ? custom : a.ServiceItem?.DefaultPrice ?? 0,
                a.StripeSubscriptionItemId is not null,
                a.StartedAt)).ToList(),
            Orders = orders,
            Gifts = items.Where(i => i.Kind == OfferItemKind.Gift).ToList(),
            Charges = items.Where(i => i.Kind == OfferItemKind.Charge).ToList(),
            Sms = await credits.GetSmsStatusAsync(tenantId),
            ExtraStorageGb = extra.StorageGb,
            ExtraBandwidthGb = extra.BandwidthGb
        };
    }

    /// <summary>Usługi z katalogu do szybkiego dodania (z ceną dla tego trenera i podpowiedzią efektu).</summary>
    public async Task<List<OfferCatalogOption>> CatalogAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var custom = await db.TenantServicePrices.AsNoTracking().Where(p => p.TenantId == tenantId)
            .ToDictionaryAsync(p => p.ServiceItemId, p => p.CustomPrice);
        var items = await db.ServiceItems.AsNoTracking().Where(i => i.IsActive)
            .OrderBy(i => i.Category).ThenBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync();
        return items.Select(i => new OfferCatalogOption(
            i.Id, i.Name, i.Description,
            custom.TryGetValue(i.Id, out var price) ? price : i.DefaultPrice,
            i.PriceType == "monthly" ? OfferBilling.Monthly : i.PriceType == "yearly" ? OfferBilling.Yearly : OfferBilling.OneTime,
            i.FulfillmentType switch
            {
                "credit_sms" => OfferEffect.Sms,
                "credit_cdn_storage" => OfferEffect.VideoStorageGb,
                "credit_cdn_bandwidth" => OfferEffect.VideoBandwidthGb,
                _ => OfferEffect.None
            },
            i.CreditAmount)).ToList();
    }

    public async Task<(bool Ok, string Message)> AddAsync(int tenantId, OfferItemInput input, string? by)
    {
        var name = input.Name.Trim();
        if (name.Length == 0) return (false, "Podaj nazwę pozycji.");
        if (input.Quantity is < 1 or > 100_000) return (false, "Ilość musi być od 1 do 100 000.");
        if (input.UnitPrice < 0) return (false, "Cena nie może być ujemna.");
        if (input.Kind == OfferItemKind.Charge && input.UnitPrice == 0) return (false, "Pozycja płatna musi mieć cenę — bez ceny dodaj ją jako gratis.");
        if (input.Kind is not (OfferItemKind.Gift or OfferItemKind.Charge)) return (false, "Nieznany rodzaj pozycji.");
        if (input.Billing is not (OfferBilling.OneTime or OfferBilling.Monthly or OfferBilling.Yearly)) return (false, "Nieznany sposób rozliczenia.");
        if (input.Effect is not (OfferEffect.None or OfferEffect.Sms or OfferEffect.VideoStorageGb or OfferEffect.VideoBandwidthGb)) return (false, "Nieznany efekt.");
        if (input.Effect != OfferEffect.None && input.EffectAmount is < 1 or > 1_000_000) return (false, "Podaj, ile SMS-ów / GB daje pozycja.");

        var starts = input.StartsAt is { } s ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : DateTime.UtcNow;
        DateTime? ends = input.EndsAt is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null;
        if (ends is not null && ends <= starts) return (false, "Data końca musi być późniejsza niż początek.");

        await using var db = dbFactory.CreateDbContext();
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId)) return (false, "Nie ma takiego trenera.");

        var item = new TenantOfferItem
        {
            TenantId = tenantId,
            Kind = input.Kind,
            ServiceItemId = input.ServiceItemId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            InternalNote = string.IsNullOrWhiteSpace(input.InternalNote) ? null : input.InternalNote.Trim(),
            Quantity = input.Quantity,
            UnitPrice = Math.Round(input.UnitPrice, 2),
            Billing = input.Billing,
            Effect = input.Effect,
            EffectAmount = input.Effect == OfferEffect.None ? 0 : input.EffectAmount,
            StartsAt = starts,
            EndsAt = ends,
            CreatedBy = by
        };
        db.TenantOfferItems.Add(item);
        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenantId,
            EventType = TenantEventTypes.OfferChanged,
            Detail = $"{(item.Kind == OfferItemKind.Gift ? "Gratis" : "Dodatkowo płatne")}: {Describe(item)}"
        });
        await db.SaveChangesAsync();

        var applied = await ApplyDueEffectsAsync(item.Id);
        await tenants.PushEntitlementsAsync(tenantId);
        logger.LogInformation("Oferta trenera {Tenant}: dodano {Kind} „{Name}” ({By}).", tenantId, item.Kind, item.Name, by);
        return (true, applied ?? "Dodano do oferty.");
    }

    /// <summary>Kończy pozycję teraz (np. gratis na czas nieokreślony albo rezygnacja z opłaty cyklicznej).</summary>
    public async Task<(bool Ok, string Message)> EndAsync(int tenantId, int itemId)
    {
        await using var db = dbFactory.CreateDbContext();
        var item = await db.TenantOfferItems.FirstOrDefaultAsync(i => i.Id == itemId && i.TenantId == tenantId);
        if (item is null || item.CancelledAt is not null) return (false, "Ta pozycja jest już zakończona.");
        var now = DateTime.UtcNow;
        item.CancelledAt = now;
        if (item.EndsAt is null || item.EndsAt > now) item.EndsAt = now;
        db.TenantEvents.Add(new TenantEvent { TenantId = tenantId, EventType = TenantEventTypes.OfferChanged, Detail = $"Zakończono: {item.Name}" });
        await db.SaveChangesAsync();
        await tenants.PushEntitlementsAsync(tenantId);
        return (true, item.Effect == OfferEffect.Sms && item.Billing == OfferBilling.OneTime && item.EffectAppliedAt is not null
            ? "Zakończono. Dopisane już SMS-y zostają na koncie trenera."
            : "Zakończono.");
    }

    /// <summary>Jednorazowa opłata: oznacza jako doliczoną do rachunku (albo cofa to oznaczenie).</summary>
    public async Task<(bool Ok, string Message)> ToggleSettledAsync(int tenantId, int itemId)
    {
        await using var db = dbFactory.CreateDbContext();
        var item = await db.TenantOfferItems.FirstOrDefaultAsync(i => i.Id == itemId && i.TenantId == tenantId && i.Kind == OfferItemKind.Charge);
        if (item is null) return (false, "Nie ma takiej opłaty.");
        item.SettledAt = item.SettledAt is null ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        return (true, item.SettledAt is null ? "Oznaczono jako do rozliczenia." : "Oznaczono jako rozliczone.");
    }

    /// <summary>Usuwa pomyłkę z oferty. Dopisane już SMS-y zostają (usunięcie ich nie cofa).</summary>
    public async Task<(bool Ok, string Message)> DeleteAsync(int tenantId, int itemId)
    {
        await using var db = dbFactory.CreateDbContext();
        var item = await db.TenantOfferItems.FirstOrDefaultAsync(i => i.Id == itemId && i.TenantId == tenantId);
        if (item is null) return (false, "Nie ma takiej pozycji.");
        db.TenantOfferItems.Remove(item);
        db.TenantEvents.Add(new TenantEvent { TenantId = tenantId, EventType = TenantEventTypes.OfferChanged, Detail = $"Usunięto z oferty: {item.Name}" });
        await db.SaveChangesAsync();
        await tenants.PushEntitlementsAsync(tenantId);
        return (true, item.EffectAppliedAt is not null ? "Usunięto. Dopisane już SMS-y zostają na koncie trenera." : "Usunięto.");
    }

    /// <summary>
    /// Dopisuje jednorazowe SMS-y, gdy przyszła data rozpoczęcia pozycji (od razu albo później — wtedy robi to
    /// <see cref="OfferSyncService"/>). Zwraca opis dla administratora albo null, gdy nie było czego dopisać.
    /// </summary>
    public async Task<string?> ApplyDueEffectsAsync(int itemId)
    {
        await using var db = dbFactory.CreateDbContext();
        var item = await db.TenantOfferItems.FirstOrDefaultAsync(i => i.Id == itemId);
        if (item is null || item.Effect != OfferEffect.Sms || item.Billing != OfferBilling.OneTime
            || item.EffectAppliedAt is not null || item.CancelledAt is not null || item.StartsAt > DateTime.UtcNow)
            return null;

        // Najpierw oznaczenie (warunkowe UPDATE), potem kredyty — dwa równoległe wywołania nie dopiszą podwójnie.
        var now = DateTime.UtcNow;
        var claimed = await db.TenantOfferItems.Where(i => i.Id == itemId && i.EffectAppliedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.EffectAppliedAt, now));
        if (claimed == 0) return null;
        await credits.AddCreditsAsync(item.TenantId, "sms", item.TotalEffect,
            $"Oferta: {item.Name}{(item.Kind == OfferItemKind.Gift ? " (gratis)" : "")}");
        return $"Dodano do oferty i dopisano {item.TotalEffect} SMS na konto trenera.";
    }

    /// <summary>Krótki opis pozycji: „500 SMS · jednorazowo · 0 zł (gratis)”.</summary>
    public static string Describe(TenantOfferItem i) =>
        $"{i.Name}{(i.Quantity > 1 ? $" × {i.Quantity}" : "")} · {BillingLabel(i.Billing).ToLowerInvariant()}"
        + (i.Kind == OfferItemKind.Gift ? " · gratis" : $" · {Money(i.Total)}");

    public static string BillingLabel(string billing) => billing switch
    {
        OfferBilling.Monthly => "Co miesiąc",
        OfferBilling.Yearly => "Co rok",
        _ => "Jednorazowo"
    };

    public static string BillingSuffix(string billing) => billing switch
    {
        OfferBilling.Monthly => " / mies.",
        OfferBilling.Yearly => " / rok",
        _ => ""
    };

    /// <summary>Efekt w aplikacji trenera, np. „+500 SMS (nie wygasają)”, „+200 SMS co miesiąc”, „+50 GB transferu wideo”.</summary>
    public static string? EffectLabel(TenantOfferItem i) => i.Effect switch
    {
        OfferEffect.Sms when i.Billing == OfferBilling.OneTime => $"+{i.TotalEffect} SMS (nie wygasają)",
        OfferEffect.Sms => $"+{i.TotalEffect} SMS w limicie miesięcznym",
        OfferEffect.VideoStorageGb => $"+{i.TotalEffect} GB przestrzeni na wideo",
        OfferEffect.VideoBandwidthGb => $"+{i.TotalEffect} GB transferu wideo / mies.",
        _ => null
    };

    /// <summary>
    /// Ostatni dzień obowiązywania do wyświetlenia. Koniec ustawiony datą „do (włącznie)” zapisujemy jako północ
    /// następnego dnia, więc wtedy pokazujemy dzień wcześniej; zakończenie „teraz” pokazujemy jako ten dzień.
    /// </summary>
    public static DateTime LastDayLocal(DateTime endUtc, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc), tz);
        return local.TimeOfDay == TimeSpan.Zero ? local.Date.AddDays(-1) : local.Date;
    }

    public static string Money(decimal value) =>
        value.ToString(value % 1 == 0 ? "#,0" : "#,0.00", System.Globalization.CultureInfo.GetCultureInfo("pl-PL")) + " zł";
}
