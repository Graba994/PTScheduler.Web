using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Polecenia między trenerami. Link ?ref={slug} prowadzi do kreatora: polecony dostaje od razu
/// 30 dni dłuższy okres próbny, a polecający — miesiąc gratis, gdy polecony zapłaci pierwszy raz
/// (nie przy samej rejestracji, więc fałszywe konta nic nie dają). Najwyżej 12 nagród w roku.
/// </summary>
public class ReferralService(
    IDbContextFactory<PortalDbContext> dbFactory,
    StripeService stripe,
    EmailService email,
    IConfiguration config,
    ILogger<ReferralService> logger)
{
    /// <summary>Nazwa platformy w stopce „Zrobione w …” i w e-mailach polecających.</summary>
    public const string PlatformName = "PTScheduler";
    public const int ReferredBonusDays = 30;
    public const int MaxRewardsPerYear = 12;

    /// <summary>Polecający po kodzie z linku (slug działającej aplikacji).</summary>
    public async Task<Tenant?> FindReferrerAsync(string? code)
    {
        var slug = code?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug) || slug.Length > 63) return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug && t.Status == TenantStatus.Active);
    }

    /// <summary>Dodatkowe dni okresu próbnego: z kodu zaproszenia i z polecenia.</summary>
    public static async Task<int> ExtraTrialDaysAsync(PortalDbContext db, Tenant tenant)
    {
        var invite = string.IsNullOrWhiteSpace(tenant.InviteCode) ? 0
            : await db.InviteCodes.Where(c => c.Code == tenant.InviteCode).Select(c => c.ExtraTrialDays).FirstOrDefaultAsync();
        return Math.Max(0, invite) + (tenant.ReferredByTenantId is not null ? ReferredBonusDays : 0);
    }

    /// <summary>Link polecający; bez podanego adresu bierzemy Portal:PublicUrl (ten sam co w linkach do rachunków).</summary>
    public string LinkFor(string slug, string? portalBase = null)
    {
        var portal = portalBase?.TrimEnd('/') is { Length: > 0 } b ? b
            : config.GetValue<string>("Portal:PublicUrl")?.TrimEnd('/') is { Length: > 0 } pub ? pub : "https://ptscheduler.pl";
        return $"{portal}/register?ref={Uri.EscapeDataString(slug)}";
    }

    public sealed record Stats(string Link, int Referred, int Paying, int FreeMonthsLeft, int RewardsThisYear);

    public async Task<Stats> StatsAsync(Tenant tenant, string? portalBase = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var referred = await db.Tenants.AsNoTracking().Where(t => t.ReferredByTenantId == tenant.Id && t.Status != TenantStatus.Destroyed).ToListAsync();
        var yearAgo = DateTime.UtcNow.AddYears(-1);
        return new Stats(LinkFor(tenant.Slug, portalBase), referred.Count, referred.Count(t => t.ReferralRewardedAt is not null),
            tenant.FreeMonths, referred.Count(t => t.ReferralRewardedAt >= yearAgo));
    }

    /// <summary>
    /// Nagroda dla polecającego po pierwszej płatności poleconego: przy Stripe — saldo na kolejną fakturę
    /// (wartość miesiąca planu), przy rachunkach — darmowy miesiąc odjęty na najbliższym rachunku.
    /// </summary>
    public async Task RewardAsync(int referredTenantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var referred = await db.Tenants.FirstOrDefaultAsync(t => t.Id == referredTenantId);
        if (referred?.ReferredByTenantId is not { } referrerId || referred.ReferralRewardedAt is not null) return;
        var referrer = await db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == referrerId);
        referred.ReferralRewardedAt = DateTime.UtcNow;

        if (referrer is null || referrer.Status is TenantStatus.Destroyed
            || string.Equals(referrer.OwnerEmail, referred.OwnerEmail, StringComparison.OrdinalIgnoreCase))
        {
            await db.SaveChangesAsync();
            return;
        }
        var yearAgo = DateTime.UtcNow.AddYears(-1);
        if (await db.Tenants.CountAsync(t => t.ReferredByTenantId == referrerId && t.ReferralRewardedAt >= yearAgo && t.Id != referred.Id) >= MaxRewardsPerYear)
        {
            db.TenantEvents.Add(new TenantEvent { TenantId = referrerId, EventType = TenantEventTypes.ReferralReward, Detail = $"Polecenie {referred.Slug} opłacone — limit {MaxRewardsPerYear} nagród w roku wyczerpany" });
            await db.SaveChangesAsync();
            return;
        }

        var monthValue = referrer.Plan is { } plan
            ? (referrer.BillingInterval == "yearly" && plan.YearlyPrice is > 0 ? Math.Round(plan.YearlyPrice.Value / 12m, 2) : plan.MonthlyPrice)
            : 0m;
        string how;
        if (!string.IsNullOrWhiteSpace(referrer.StripeSubscriptionId) && monthValue > 0
            && await stripe.AddBalanceCreditAsync(referrer.StripeCustomerId, monthValue, $"Miesiąc gratis za polecenie: {referred.CompanyName}"))
            how = $"saldo Stripe −{monthValue:0.00} zł";
        else
        {
            referrer.FreeMonths++;
            how = "darmowy miesiąc na rachunku";
        }
        db.TenantEvents.Add(new TenantEvent { TenantId = referrerId, EventType = TenantEventTypes.ReferralReward, Detail = $"Polecenie {referred.Slug} opłacone — {how}" });
        await db.SaveChangesAsync();
        logger.LogInformation("Nagroda za polecenie: {Referrer} za {Referred} ({How}).", referrer.Slug, referred.Slug, how);

        var (first, _) = TenantSetupPayload.SplitName(referrer.OwnerName);
        _ = email.SendAsync(referrer.OwnerEmail, "Masz miesiąc gratis — dziękujemy za polecenie",
            email.ReferralRewardEmailBody(first ?? referrer.OwnerName, referred.CompanyName, LinkFor(referrer.Slug)));
    }
}
