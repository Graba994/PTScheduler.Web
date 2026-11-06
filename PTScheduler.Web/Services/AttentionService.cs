using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Wymaga uwagi”: jedno miejsce na klientów, którym warto się przyjrzeć — mogą odejść, zalegają z płatnością,
/// pasują do wolnego okienka, kończy im się pakiet. Liczby do zakładek (na krótko w pamięci, bo liczenie okienek kosztuje).
/// </summary>
public class AttentionService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock, RetentionService retention,
    PTScheduler.Infrastructure.Services.DebtService debts, SlotInviteService slots)
{
    public sealed record Counts(int Risks, int Debts, int Slots, int Packages);

    public sealed record EndingPackage(int ClientId, string Name, string? Phone, string PackageName, int Left, DateTime? ExpiresAt);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, Counts Counts)> Cache = new();

    public async Task<Counts> CountsAsync(string userId, bool isAdmin, bool owner)
    {
        var key = $"{userId}|{isAdmin}|{owner}";
        if (Cache.TryGetValue(key, out var hit) && clock.UtcNow - hit.At < TimeSpan.FromMinutes(5)) return hit.Counts;
        int risks = 0, debtCount = 0, slotCount = 0, packages = 0;
        try { debtCount = (await debts.GetAsync(userId, isAdmin)).Count; } catch { }
        if (owner)
        {
            try { risks = (await retention.GetRisksAsync(isAdmin ? null : userId)).Count; } catch { }
            try { slotCount = (await slots.GetAsync(userId)).Count; } catch { }
            try { packages = (await EndingPackagesAsync(userId, isAdmin)).Count; } catch { }
        }
        var counts = new Counts(risks, debtCount, slotCount, packages);
        Cache[key] = (clock.UtcNow, counts);
        if (Cache.Count > 200) Cache.Clear();
        return counts;
    }

    public static void Invalidate() => Cache.Clear();

    /// <summary>Aktywne pakiety z najwyżej jednym wejściem albo ważne jeszcze najwyżej tydzień.</summary>
    public async Task<List<EndingPackage>> EndingPackagesAsync(string userId, bool isAdmin)
    {
        var utcNow = clock.UtcNow;
        var weekAhead = utcNow.AddDays(7);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.SessionPackages.AsNoTracking()
            .Where(p => p.Status == PackageStatus.Active && !p.IsHidden && p.TotalSessions > 0
                && (isAdmin || p.Client.TrainerUserId == userId)
                && (p.TotalSessions - p.UsedSessions <= 1 || (p.ExpiresAt != null && p.ExpiresAt <= weekAhead && p.ExpiresAt > utcNow)))
            .Select(p => new { p.ClientId, p.Client.FirstName, p.Client.LastName, p.Client.Phone, p.Name, Left = p.TotalSessions - p.UsedSessions, p.ExpiresAt })
            .ToListAsync();
        return rows.GroupBy(r => r.ClientId)
            .Select(g => g.OrderBy(r => r.Left).First())
            .Select(r => new EndingPackage(r.ClientId, $"{r.FirstName} {r.LastName}".Trim(), r.Phone, r.Name, Math.Max(0, r.Left), r.ExpiresAt))
            .OrderBy(r => r.Left).ThenBy(r => r.ExpiresAt ?? DateTime.MaxValue).ToList();
    }
}
