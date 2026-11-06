using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// „Kto mi zalega”: treningi poza pakietem, które już się odbyły i nie są opłacone, oraz nieopłacone pakiety —
/// pogrupowane po kliencie, najstarsze zaległości na górze.
/// </summary>
public sealed class DebtService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock)
{
    public sealed record Item(string Kind, int Id, string Label, DateTime At, decimal? Amount);
    public sealed record ClientDebt(int ClientId, string Name, string? Phone, List<Item> Items)
    {
        public decimal Total => Items.Sum(i => i.Amount ?? 0);
        public DateTime Oldest => Items.Min(i => i.At);
        public bool HasUnknownAmount => Items.Any(i => i.Amount is null);
    }

    public async Task<List<ClientDebt>> GetAsync(string trainerUserId, bool isAdmin, int? clientId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await LoadAsync(db, clock.LocalNow, isAdmin ? null : trainerUserId, clientId);
    }

    public static async Task<List<ClientDebt>> LoadAsync(ApplicationDbContext db, DateTime localNow, string? trainerUserId, int? clientId)
    {
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.OffPackagePayment != null && s.PaidAt == null && !s.AwaitingApproval
                        && (s.Status == SessionStatus.Completed || (s.Status == SessionStatus.Scheduled && s.StartTime < localNow))
                        && (trainerUserId == null || s.TrainerUserId == trainerUserId)
                        && (clientId == null || s.ClientId == clientId))
            .Select(s => new { s.Id, s.ClientId, s.StartTime, Type = s.SessionType.Name, Price = s.SessionType.SinglePrice })
            .ToListAsync();
        var packages = await db.SessionPackages.AsNoTracking()
            .Where(p => !p.IsPaid && p.Status != PackageStatus.Cancelled && !p.IsHidden
                        && (trainerUserId == null || p.Client.TrainerUserId == trainerUserId)
                        && (clientId == null || p.ClientId == clientId))
            .Select(p => new { p.Id, p.ClientId, p.Name, p.PurchasedAt, Amount = p.PricePerSession * p.TotalSessions })
            .ToListAsync();

        var ids = sessions.Select(s => s.ClientId).Concat(packages.Select(p => p.ClientId)).Distinct().ToList();
        if (ids.Count == 0) return [];
        var clients = await db.Clients.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, Name = c.FirstName + " " + c.LastName, c.Phone }).ToDictionaryAsync(c => c.Id);

        return ids.Where(clients.ContainsKey).Select(id =>
        {
            var items = sessions.Where(s => s.ClientId == id)
                .Select(s => new Item("session", s.Id, $"{s.Type} {s.StartTime:d.MM}", s.StartTime, s.Price))
                .Concat(packages.Where(p => p.ClientId == id)
                    .Select(p => new Item("package", p.Id, $"Pakiet „{p.Name}”", p.PurchasedAt.ToLocalTime(), p.Amount > 0 ? p.Amount : null)))
                .OrderBy(i => i.At).ToList();
            return new ClientDebt(id, clients[id].Name.Trim(), clients[id].Phone, items);
        }).OrderBy(d => d.Oldest).ToList();
    }
}
