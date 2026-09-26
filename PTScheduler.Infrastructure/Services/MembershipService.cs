using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Memberships;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class MembershipService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IWebPushService push,
    IAppClock clock,
    ILogger<MembershipService> logger) : IMembershipService
{
    // ── karnety (oferta) ───────────────────────────────────────────────────

    public async Task<List<MembershipPlanDto>> GetPlansAsync(bool onlyActive = false, bool onlyShop = false)
    {
        await using var db = dbFactory.CreateDbContext();
        var plans = await db.MembershipPlans.AsNoTracking()
            .Include(p => p.SessionType)
            .Where(p => (!onlyActive || p.IsActive) && (!onlyShop || p.AvailableInShop))
            .OrderBy(p => p.Price)
            .ToListAsync();
        var counts = await db.Memberships.AsNoTracking()
            .Where(m => m.Status == MembershipStatus.Active || m.Status == MembershipStatus.PastDue)
            .GroupBy(m => m.PlanId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return plans.Select(p => new MembershipPlanDto
        {
            Id = p.Id, Name = p.Name, Description = p.Description,
            SessionTypeId = p.SessionTypeId, SessionTypeName = p.SessionType.Name,
            SessionsPerPeriod = p.SessionsPerPeriod, PeriodMonths = p.PeriodMonths,
            Price = p.Price, Currency = p.Currency, CarryOverUnused = p.CarryOverUnused,
            AvailableInShop = p.AvailableInShop, IsActive = p.IsActive,
            ActiveMembers = counts.GetValueOrDefault(p.Id)
        }).ToList();
    }

    public async Task<int> SavePlanAsync(MembershipPlanDto dto, string userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("Podaj nazwę karnetu.");
        if (dto.SessionsPerPeriod < 1) throw new InvalidOperationException("Karnet musi zawierać co najmniej jedną sesję.");
        if (dto.Price < 0) throw new InvalidOperationException("Cena nie może być ujemna.");

        await using var db = dbFactory.CreateDbContext();
        var plan = dto.Id > 0 ? await db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == dto.Id) : null;
        if (plan is null)
        {
            plan = new MembershipPlan { CreatedByUserId = userId };
            db.MembershipPlans.Add(plan);
        }
        plan.Name = dto.Name.Trim();
        plan.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        plan.SessionTypeId = dto.SessionTypeId;
        plan.SessionsPerPeriod = dto.SessionsPerPeriod;
        plan.PeriodMonths = Math.Clamp(dto.PeriodMonths, 1, 12);
        plan.Price = dto.Price;
        plan.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "PLN" : dto.Currency;
        plan.CarryOverUnused = dto.CarryOverUnused;
        plan.AvailableInShop = dto.AvailableInShop;
        plan.IsActive = dto.IsActive;
        await db.SaveChangesAsync();
        return plan.Id;
    }

    // ── subskrypcje ───────────────────────────────────────────────────────

    public async Task<List<MembershipDto>> GetMembershipsAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.Memberships.AsNoTracking()
            .Include(m => m.Client).Include(m => m.Plan)
            .Include(m => m.Periods).ThenInclude(p => p.Package)
            .Where(m => trainerUserId == null || m.Client.TrainerUserId == trainerUserId)
            .OrderBy(m => m.Status).ThenBy(m => m.NextBillingDate)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<List<MembershipDto>> GetClientMembershipsAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.Memberships.AsNoTracking()
            .Include(m => m.Client).Include(m => m.Plan)
            .Include(m => m.Periods).ThenInclude(p => p.Package)
            .Where(m => m.ClientId == clientId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<(bool Ok, string? Error)> SubscribeAsync(int clientId, int planId, DateOnly start, decimal? priceOverride)
    {
        await using var db = dbFactory.CreateDbContext();
        var plan = await db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan is null) return (false, "Karnet nie istnieje albo jest nieaktywny.");
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return (false, "Nie znaleziono klienta.");
        var hasActive = await db.Memberships.AnyAsync(m => m.ClientId == clientId && m.PlanId == planId
            && m.Status != MembershipStatus.Cancelled);
        if (hasActive) return (false, "Klient ma już ten karnet.");

        var membership = new Membership
        {
            ClientId = clientId,
            PlanId = planId,
            Status = MembershipStatus.Active,
            PriceOverride = priceOverride,
            CurrentPeriodStart = start,
            NextBillingDate = MembershipBilling.NextStart(start, plan.PeriodMonths),
            CreatedAt = clock.UtcNow
        };
        db.Memberships.Add(membership);
        MembershipLedger.CreatePeriod(db, clock, membership, plan, client, start, carryOver: 0);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task MarkPeriodPaidAsync(int periodId, string reference)
    {
        await using var db = dbFactory.CreateDbContext();
        await MembershipLedger.MarkPaidAsync(db, clock, periodId, reference);
        await db.SaveChangesAsync();
    }

    public async Task WaivePeriodAsync(int periodId)
    {
        await using var db = dbFactory.CreateDbContext();
        var period = await db.MembershipPeriods.Include(p => p.Membership).Include(p => p.Package)
            .FirstOrDefaultAsync(p => p.Id == periodId);
        if (period is null || period.Status != MembershipPeriodStatus.Due) return;
        period.Status = MembershipPeriodStatus.Waived;
        if (period.Package is not null) period.Package.IsPaid = true;
        await MembershipLedger.RefreshStatusAsync(db, clock, period.Membership);
        await db.SaveChangesAsync();
    }

    public async Task CancelAsync(int membershipId, bool immediately)
    {
        await using var db = dbFactory.CreateDbContext();
        var m = await db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId);
        if (m is null || m.Status == MembershipStatus.Cancelled) return;
        if (immediately)
        {
            m.Status = MembershipStatus.Cancelled;
            m.CancelledAt = clock.UtcNow;
        }
        else
        {
            m.CancelAtPeriodEnd = true;
        }
        await db.SaveChangesAsync();
    }

    public async Task ResumeCancelledAsync(int membershipId)
    {
        await using var db = dbFactory.CreateDbContext();
        var m = await db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId);
        if (m is null || m.Status == MembershipStatus.Cancelled) return;
        m.CancelAtPeriodEnd = false;
        await db.SaveChangesAsync();
    }

    public async Task PauseAsync(int membershipId)
    {
        await using var db = dbFactory.CreateDbContext();
        var m = await db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId);
        if (m is null || m.Status is MembershipStatus.Cancelled or MembershipStatus.Paused) return;
        m.Status = MembershipStatus.Paused;
        await db.SaveChangesAsync();
    }

    public async Task ResumeAsync(int membershipId)
    {
        await using var db = dbFactory.CreateDbContext();
        var m = await db.Memberships.Include(x => x.Periods).FirstOrDefaultAsync(x => x.Id == membershipId);
        if (m is null || m.Status != MembershipStatus.Paused) return;
        // Po przerwie rozliczenia ruszają od dziś — bez „zaległych” okresów za czas pauzy.
        var today = clock.Today;
        if (m.NextBillingDate < today) m.NextBillingDate = today;
        m.Status = MembershipStatus.Active;
        await MembershipLedger.RefreshStatusAsync(db, clock, m);
        await db.SaveChangesAsync();
    }

    // ── rozliczenia cykliczne ─────────────────────────────────────────────

    public async Task<int> ProcessBillingAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        var changes = 0;
        var notify = new List<(string UserId, PushMessageDto Message)>();

        await using var db = dbFactory.CreateDbContext();
        var due = await db.Memberships
            .Include(m => m.Plan).Include(m => m.Client)
            .Include(m => m.Periods).ThenInclude(p => p.Package)
            .Where(m => (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.PastDue)
                        && m.NextBillingDate <= today)
            .ToListAsync(ct);

        foreach (var m in due)
        {
            if (m.CancelAtPeriodEnd)
            {
                m.Status = MembershipStatus.Cancelled;
                m.CancelledAt = clock.UtcNow;
                changes++;
                continue;
            }
            // Pętla nadrabia okresy, gdyby usługa nie działała przez dłuższy czas.
            while (m.NextBillingDate <= today)
            {
                var carry = 0;
                var previous = m.Periods.OrderByDescending(p => p.PeriodStart).FirstOrDefault()?.Package;
                if (m.Plan.CarryOverUnused && previous is not null && previous.Status == PackageStatus.Active)
                {
                    carry = Math.Max(0, previous.TotalSessions - previous.UsedSessions);
                    previous.TotalSessions = previous.UsedSessions;
                    previous.Status = PackageStatus.Depleted;
                }
                var start = m.NextBillingDate;
                var period = MembershipLedger.CreatePeriod(db, clock, m, m.Plan, m.Client, start, carry);
                m.CurrentPeriodStart = start;
                m.NextBillingDate = MembershipBilling.NextStart(start, m.Plan.PeriodMonths);
                changes++;
                notify.Add((m.Client.ApplicationUserId, new PushMessageDto
                {
                    Title = $"🗓️ Nowy okres karnetu „{m.Plan.Name}”",
                    Body = $"Masz {m.Plan.SessionsPerPeriod} sesji do {MembershipBilling.PeriodEnd(start, m.Plan.PeriodMonths):dd.MM}. " +
                           $"Do zapłaty {period.Amount:0.00} {m.Plan.Currency} do {period.DueDate:dd.MM}.",
                    Url = "/my/memberships"
                }));
            }
        }

        // Zaległości: okres nieopłacony po terminie → PastDue + jedno przypomnienie.
        var overdue = await db.MembershipPeriods
            .Include(p => p.Membership).ThenInclude(m => m.Client)
            .Include(p => p.Membership).ThenInclude(m => m.Plan)
            .Where(p => p.Status == MembershipPeriodStatus.Due && p.DueDate < today && p.ReminderSentAt == null
                        && p.Membership.Status != MembershipStatus.Cancelled)
            .ToListAsync(ct);
        foreach (var p in overdue)
        {
            p.ReminderSentAt = clock.UtcNow;
            if (p.Membership.Status == MembershipStatus.Active) p.Membership.Status = MembershipStatus.PastDue;
            changes++;
            notify.Add((p.Membership.Client.ApplicationUserId, new PushMessageDto
            {
                Title = "💳 Przypomnienie o płatności za karnet",
                Body = $"„{p.Membership.Plan.Name}” — {p.Amount:0.00} {p.Membership.Plan.Currency} (termin minął {p.DueDate:dd.MM}).",
                Url = "/my/memberships"
            }));
            if (!string.IsNullOrEmpty(p.Membership.Client.TrainerUserId))
            {
                notify.Add((p.Membership.Client.TrainerUserId, new PushMessageDto
                {
                    Title = $"Zaległa płatność: {p.Membership.Client.FirstName} {p.Membership.Client.LastName}".Trim(),
                    Body = $"Karnet „{p.Membership.Plan.Name}”, {p.Amount:0.00} {p.Membership.Plan.Currency}.",
                    Url = $"/clients/{p.Membership.ClientId}"
                }));
            }
        }

        if (changes > 0) await db.SaveChangesAsync(ct);

        foreach (var (userId, message) in notify)
        {
            try { await push.SendAsync(userId, message); }
            catch (Exception ex) { logger.LogWarning(ex, "Membership push to {UserId} failed.", userId); }
        }
        return changes;
    }

    // ── pomocnicze ─────────────────────────────────────────────────────────

    private static MembershipDto Map(Membership m) => new()
    {
        Id = m.Id,
        ClientId = m.ClientId,
        ClientName = $"{m.Client.FirstName} {m.Client.LastName}".Trim(),
        PlanId = m.PlanId,
        PlanName = m.Plan.Name,
        Status = m.Status,
        Price = m.PriceOverride ?? m.Plan.Price,
        Currency = m.Plan.Currency,
        SessionsPerPeriod = m.Plan.SessionsPerPeriod,
        PeriodMonths = m.Plan.PeriodMonths,
        CurrentPeriodStart = m.CurrentPeriodStart,
        NextBillingDate = m.NextBillingDate,
        CancelAtPeriodEnd = m.CancelAtPeriodEnd,
        UnpaidAmount = m.Periods.Where(p => p.Status == MembershipPeriodStatus.Due).Sum(p => p.Amount),
        Periods = m.Periods.OrderByDescending(p => p.PeriodStart).Select(p => new MembershipPeriodDto
        {
            Id = p.Id, PeriodStart = p.PeriodStart, PeriodEnd = p.PeriodEnd, Amount = p.Amount,
            Status = p.Status, DueDate = p.DueDate, PaidAt = p.PaidAt, PackageId = p.PackageId,
            SessionsTotal = p.Package?.TotalSessions ?? 0, SessionsUsed = p.Package?.UsedSessions ?? 0
        }).ToList()
    };
}

/// <summary>
/// Operacje na okresach karnetu wspólne dla serwisu karnetów i płatności online.
/// </summary>
internal static class MembershipLedger
{
    public static MembershipPeriod CreatePeriod(ApplicationDbContext db, IAppClock clock, Membership m, MembershipPlan plan, Client client,
        DateOnly start, int carryOver)
    {
        var end = MembershipBilling.PeriodEnd(start, plan.PeriodMonths);
        var amount = m.PriceOverride ?? plan.Price;
        var sessions = plan.SessionsPerPeriod + carryOver;
        var package = new SessionPackage
        {
            ClientId = client.Id,
            CreatedByUserId = plan.CreatedByUserId,
            Name = $"{plan.Name} ({start:MM.yyyy})",
            SessionTypeId = plan.SessionTypeId,
            TotalSessions = sessions,
            PricePerSession = sessions > 0 ? Math.Round(amount / sessions, 2) : amount,
            IsPaid = amount == 0,
            PurchasedAt = clock.UtcNow,
            // Pakiet ważny do końca ostatniego dnia okresu (zegar studia).
            ExpiresAt = clock.ToUtc(end.ToDateTime(new TimeOnly(23, 59))),
            Status = PackageStatus.Active,
            Notes = carryOver > 0 ? $"Karnet cykliczny (+{carryOver} z poprzedniego okresu)" : "Karnet cykliczny"
        };
        db.SessionPackages.Add(package);
        var period = new MembershipPeriod
        {
            Membership = m,
            PeriodStart = start,
            PeriodEnd = end,
            Amount = amount,
            Status = amount == 0 ? MembershipPeriodStatus.Waived : MembershipPeriodStatus.Due,
            DueDate = MembershipBilling.DueDate(start),
            Package = package
        };
        db.MembershipPeriods.Add(period);
        return period;
    }

    public static async Task RefreshStatusAsync(ApplicationDbContext db, IAppClock clock, Membership m)
    {
        if (m.Status is MembershipStatus.Cancelled or MembershipStatus.Paused) return;
        var today = clock.Today;
        var candidates = await db.MembershipPeriods
            .Where(p => p.MembershipId == m.Id && p.Status == MembershipPeriodStatus.Due && p.DueDate < today)
            .Select(p => p.Id)
            .ToListAsync();
        // Uwzględnij zmiany jeszcze niezapisane (np. właśnie opłacony okres).
        var overdue = candidates.Any(id =>
            db.MembershipPeriods.Local.FirstOrDefault(x => x.Id == id) is not { } local
            || local.Status == MembershipPeriodStatus.Due);
        // Pending (zakup w sklepie) przechodzi w Active po pierwszej płatności.
        m.Status = overdue ? MembershipStatus.PastDue : MembershipStatus.Active;
    }

    /// <summary>Oznacza okres jako opłacony (płatność online albo ręczna).</summary>
    public static async Task MarkPaidAsync(ApplicationDbContext db, IAppClock clock, int periodId, string reference)
    {
        var period = await db.MembershipPeriods
            .Include(p => p.Membership).Include(p => p.Package)
            .FirstOrDefaultAsync(p => p.Id == periodId);
        if (period is null || period.Status == MembershipPeriodStatus.Paid) return;

        period.Status = MembershipPeriodStatus.Paid;
        period.PaidAt = clock.UtcNow;
        period.PaymentReference = reference;
        if (period.Package is not null)
        {
            period.Package.IsPaid = true;
            period.Package.PaidAt = clock.UtcNow;
            period.Package.PaymentReference = reference;
            if (period.Package.Status == PackageStatus.Cancelled) period.Package.Status = PackageStatus.Active;
        }
        await RefreshStatusAsync(db, clock, period.Membership);
    }

    /// <summary>
    /// Realizacja opłaconego zamówienia karnetu: opłata okresu albo zakup karnetu
    /// w sklepie (nowa subskrypcja od dziś z opłaconym pierwszym okresem).
    /// </summary>
    public static async Task FulfilOrderAsync(ApplicationDbContext db, IAppClock clock, Order order)
    {
        if (order.MembershipPeriodId is int periodId)
        {
            await MarkPaidAsync(db, clock, periodId, order.ExtOrderId);
            return;
        }
        if (order.MembershipPlanId is not int planId) return;
        var plan = await db.MembershipPlans.FirstOrDefaultAsync(p => p.Id == planId);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == order.ApplicationUserId);
        if (plan is null || client is null) return;
        if (await db.MembershipPeriods.AnyAsync(p => p.PaymentReference == order.ExtOrderId)) return;

        var start = clock.Today;
        var membership = new Membership
        {
            ClientId = client.Id,
            PlanId = plan.Id,
            Status = MembershipStatus.Active,
            PriceOverride = order.Amount != plan.Price ? order.Amount : null,
            CurrentPeriodStart = start,
            NextBillingDate = MembershipBilling.NextStart(start, plan.PeriodMonths),
            CreatedAt = clock.UtcNow
        };
        db.Memberships.Add(membership);
        var period = CreatePeriod(db, clock, membership, plan, client, start, carryOver: 0);
        period.Status = MembershipPeriodStatus.Paid;
        period.PaidAt = clock.UtcNow;
        period.PaymentReference = order.ExtOrderId;
        if (period.Package is not null)
        {
            period.Package.IsPaid = true;
            period.Package.PaidAt = clock.UtcNow;
            period.Package.PaymentReference = order.ExtOrderId;
        }
    }
}
