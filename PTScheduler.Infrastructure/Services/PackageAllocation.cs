using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Nowy pakiet od razu obsługuje wizyty, które czekały na pakiet.
/// Pakiet osoby: każda jej czekająca wizyta pobiera 1.
/// Pakiet pary: każdy wspólny trening tej pary pobiera 1 (pierwsza wizyta pobiera, druga współdzieli).
/// Pomija wizyty w trakcie płatności online (HoldUntil) — te opłaca zamówienie.
/// </summary>
internal static class PackageAllocation
{
    public static async Task FillAwaitingAsync(ApplicationDbContext db, SessionPackage package)
    {
        if (package.PartnerClientId is int partnerId)
        {
            var pair = new HashSet<int> { package.ClientId, partnerId };
            var waiting = await db.Sessions
                .Where(s => s.PairGroupId != null && s.SessionTypeId == package.SessionTypeId
                            && (s.ClientId == package.ClientId || s.ClientId == partnerId)
                            && s.Status == SessionStatus.AwaitingPackage && s.HoldUntil == null)
                .OrderBy(s => s.StartTime).ThenBy(s => s.Id)
                .ToListAsync();
            var groupIds = waiting.Select(s => s.PairGroupId!.Value).Distinct().ToList();
            var members = await db.Sessions
                .Where(s => s.PairGroupId != null && groupIds.Contains(s.PairGroupId.Value))
                .Select(s => new { s.PairGroupId, s.ClientId })
                .ToListAsync();

            foreach (var group in waiting.GroupBy(s => s.PairGroupId!.Value))
            {
                if (package.UsedSessions >= package.TotalSessions) break;
                // Tylko treningi dokładnie tej pary.
                var clients = members.Where(m => m.PairGroupId == group.Key).Select(m => m.ClientId).ToHashSet();
                if (!clients.SetEquals(pair)) continue;
                var first = true;
                foreach (var s in group)
                {
                    s.PackageId = package.Id;
                    s.Status = SessionStatus.Scheduled;
                    ClearOffPackage(s);
                    s.SharesPackageSlot = !first;
                    s.PackageRefunded = false;
                    first = false;
                }
                package.UsedSessions++;
            }
        }
        else
        {
            var waiting = await db.Sessions
                .Where(s => s.ClientId == package.ClientId && s.SessionTypeId == package.SessionTypeId
                            && s.Status == SessionStatus.AwaitingPackage && s.HoldUntil == null)
                .OrderBy(s => s.StartTime)
                .ToListAsync();
            foreach (var s in waiting)
            {
                if (package.UsedSessions >= package.TotalSessions) break;
                s.PackageId = package.Id;
                s.Status = SessionStatus.Scheduled;
                ClearOffPackage(s);
                package.UsedSessions++;
            }
        }

        if (package.UsedSessions >= package.TotalSessions && package.Status == PackageStatus.Active)
            package.Status = PackageStatus.Depleted;
    }

    // Wizyta czekająca na akceptację albo na płatność online — pakiet ją opłaca, więc prośba i blokada znikają.
    private static void ClearOffPackage(Session s)
    {
        s.OffPackagePayment = null;
        s.AwaitingApproval = false;
        s.HoldUntil = null;
    }
}
