using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>Zapis wyboru klienta „Powiadom mnie o wolnym terminie” (dni i godziny). Powiadomienia wysyła <see cref="SlotWatchNotifier"/>.</summary>
public class SlotWatchService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public sealed record State(bool Active, int Days, TimeOnly From, TimeOnly To);

    public async Task<State?> GetAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var w = await db.SlotWatches.AsNoTracking().FirstOrDefaultAsync(x => x.ClientId == clientId);
        return w is null ? null : new(w.IsActive, w.Days, w.FromTime, w.ToTime);
    }

    public async Task SaveAsync(int clientId, int days, TimeOnly from, TimeOnly to)
    {
        if ((days & 0b1111111) == 0) throw new ArgumentException("Wybierz przynajmniej jeden dzień.");
        if (to <= from) throw new ArgumentException("Godzina „do” musi być późniejsza niż „od”.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var w = await db.SlotWatches.FirstOrDefaultAsync(x => x.ClientId == clientId);
        if (w is null) db.SlotWatches.Add(w = new SlotWatch { ClientId = clientId });
        w.Days = days & 0b1111111;
        w.FromTime = from;
        w.ToTime = to;
        w.IsActive = true;
        await db.SaveChangesAsync();
    }

    public async Task TurnOffAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SlotWatches.Where(x => x.ClientId == clientId).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
    }
}
