using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Lejek trenerów na Pulpicie: wejście do kreatora → start → konto → zgłoszenie → uruchomienie →
/// aktywacja (3 klientów i pierwszy trening) → pierwsza płatność. Pierwsze trzy kroki to dzienne
/// liczniki (bez danych osobowych), reszta wynika z dat zapisanych przy trenerach.
/// </summary>
public class FunnelService(IDbContextFactory<PortalDbContext> dbFactory, ILogger<FunnelService> logger)
{
    public async Task TrackAsync(string kind)
    {
        try
        {
            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            await using var db = await dbFactory.CreateDbContextAsync();
            var updated = await db.FunnelCounters.Where(c => c.Day == day && c.Kind == kind)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Count, c => c.Count + 1));
            if (updated > 0) return;
            db.FunnelCounters.Add(new FunnelCounter { Day = day, Kind = kind, Count = 1 });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Ktoś równolegle dodał wiersz na dziś — wystarczy zwiększyć licznik.
                await using var retry = await dbFactory.CreateDbContextAsync();
                await retry.FunnelCounters.Where(c => c.Day == day && c.Kind == kind)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.Count, c => c.Count + 1));
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "Licznik lejka {Kind} nie zapisany.", kind); }
    }

    public sealed record Stage(string Key, string Label, string Hint, int Count);

    public async Task<List<Stage>> BuildAsync(int days)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var sinceDay = DateOnly.FromDateTime(since);
        await using var db = await dbFactory.CreateDbContextAsync();
        var counters = await db.FunnelCounters.AsNoTracking().Where(c => c.Day >= sinceDay)
            .GroupBy(c => c.Kind).Select(g => new { g.Key, Count = g.Sum(c => c.Count) }).ToDictionaryAsync(x => x.Key, x => x.Count);
        int C(string k) => counters.GetValueOrDefault(k);

        // Zgłoszenia z kreatora (SetupMode „self”) — bez trenerów dodanych ręcznie w Panelu.
        var tenants = db.Tenants.AsNoTracking().Where(t => t.SetupMode == "self" && t.CreatedAt >= since);
        return
        [
            new("view", "Otworzyli kreator", "wejścia na /register", C(FunnelKinds.View)),
            new("start", "Zaczęli budować", "wpisali nazwę i „Dalej”", C(FunnelKinds.Start)),
            new("account", "Doszli do konta", "ostatni krok kreatora", C(FunnelKinds.Account)),
            new("published", "Opublikowali", "zgłoszenie zapisane", await tenants.CountAsync()),
            new("launched", "Uruchomieni", "aplikacja działa", await tenants.CountAsync(t => t.ProvisionedAt != null)),
            new("active", "Aktywni", "3 klientów i pierwszy trening", await tenants.CountAsync(t => t.ActivatedAt != null)),
            new("paying", "Płacą", "pierwsza płatność za abonament", await tenants.CountAsync(t => t.FirstPaidAt != null))
        ];
    }
}
