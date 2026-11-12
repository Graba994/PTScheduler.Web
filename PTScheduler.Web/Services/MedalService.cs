using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Medals;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Medale klientów: liczy statystyki z całej historii (treningi, serie tygodni, rekordy, staż, ankiety, plan),
/// zapisuje nowo zdobyte medale, daje gablotę z postępem i ustawienia wyglądu (styl z kreatora strony,
/// kolory z motywu aplikacji i strony).
/// </summary>
public class MedalService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ISiteContentService site,
    IBrandingService branding,
    IAppClock clock)
{
    public sealed record Settings(bool Enabled, string Style, MedalPalette Palette);

    public sealed record Item(MedalRules.Medal Medal, DateTime? EarnedAt, double Progress, int Current);

    public async Task<Settings> GetSettingsAsync(SiteContentDto? content = null)
    {
        content ??= await site.GetAsync();
        var brand = await branding.GetAsync();
        var style = MedalArt.IsStyle(content.MedalStyle) ? content.MedalStyle : "klasyczny";
        return new(content.MedalsEnabled, style, MedalArt.Palette(brand.ThemeName, content.Theme, content.AccentOverride));
    }

    public async Task<MedalStats> StatsAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sessionDays = (await db.Sessions.AsNoTracking()
                .Where(s => s.ClientId == clientId && s.Status == SessionStatus.Completed)
                .Select(s => s.StartTime).ToListAsync())
            .Select(DateOnly.FromDateTime).ToList();
        var workoutDays = await db.WorkoutLogs.AsNoTracking().Where(l => l.ClientId == clientId)
            .Select(l => l.WorkoutDate).Distinct().ToListAsync();
        var days = sessionDays.Concat(workoutDays).Distinct().ToList();

        var perExercise = (await db.WorkoutSetLogs.AsNoTracking()
                .Where(s => s.WorkoutLog!.ClientId == clientId && s.Reps > 0 && s.WeightKg > 0)
                .GroupBy(s => new { s.WorkoutLog!.ExerciseId, s.WorkoutLog.WorkoutDate })
                .Select(g => new { g.Key.ExerciseId, g.Key.WorkoutDate, Max = g.Max(s => s.WeightKg) })
                .ToListAsync())
            .Select(x => (x.ExerciseId, x.WorkoutDate, x.Max));

        var surveys = await db.SurveyResponses.CountAsync(r => r.ClientId == clientId && r.Kind == SurveyKind.PostWorkout);
        var today = clock.Today;
        var months = 0;
        if (days.Count > 0)
        {
            var first = days.Min();
            months = (today.Year - first.Year) * 12 + today.Month - first.Month - (today.Day < first.Day ? 1 : 0);
        }
        return new(days.Count, MedalRules.BestWeekStreak(days), MedalRules.CountRecords(perExercise), Math.Max(0, months), surveys, workoutDays.Count);
    }

    /// <summary>Zapisuje nowo zdobyte medale i zwraca je (pusta lista, gdy nic nowego).</summary>
    public async Task<List<MedalRules.Medal>> SyncAsync(int clientId)
    {
        var earned = MedalRules.Earned(await StatsAsync(clientId));
        if (earned.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync();
        var have = (await db.ClientMedals.Where(m => m.ClientId == clientId).Select(m => m.Key).ToListAsync()).ToHashSet();
        var fresh = earned.Where(k => !have.Contains(k)).ToList();
        if (fresh.Count == 0) return [];
        // Pierwsze uruchomienie przy dużej historii: dawne medale bez fanfar (od razu „widziane”, bez push), nowe — z gratulacjami.
        var silent = have.Count == 0 && fresh.Count > 3;
        foreach (var key in fresh)
            db.ClientMedals.Add(new ClientMedal { ClientId = clientId, Key = key, EarnedAt = clock.UtcNow, SeenAt = silent ? clock.UtcNow : null, Notified = silent });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return []; } // równoległa synchronizacja zapisała je pierwsza
        return silent ? [] : fresh.Select(k => MedalRules.Find(k)!).ToList();
    }

    public async Task<List<Item>> CabinetAsync(int clientId)
    {
        var stats = await StatsAsync(clientId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var earned = await db.ClientMedals.AsNoTracking().Where(m => m.ClientId == clientId).ToDictionaryAsync(m => m.Key, m => m.EarnedAt);
        return MedalRules.All.Select(m => new Item(m, earned.TryGetValue(m.Key, out var at) ? at : null,
            earned.ContainsKey(m.Key) ? 1 : MedalRules.Progress(m, stats), Math.Min(m.Value(stats), m.Target))).ToList();
    }

    public async Task<List<MedalRules.Medal>> UnseenAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var keys = await db.ClientMedals.AsNoTracking().Where(m => m.ClientId == clientId && m.SeenAt == null)
            .OrderBy(m => m.EarnedAt).Select(m => m.Key).ToListAsync();
        return keys.Select(MedalRules.Find).Where(m => m is not null).Select(m => m!).ToList();
    }

    public async Task MarkSeenAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = clock.UtcNow;
        await db.ClientMedals.Where(m => m.ClientId == clientId && m.SeenAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.SeenAt, now).SetProperty(m => m.Notified, true));
    }
}

/// <summary>Co 3 godziny sprawdza medale aktywnych klientów z kontem i wysyła push o nowych (gdy medale są włączone).</summary>
public class MedalBackgroundService(IServiceScopeFactory scopes, ILogger<MedalBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Sprawdzanie medali się nie udało."); }
            await Task.Delay(TimeSpan.FromHours(3), ct);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var medals = scope.ServiceProvider.GetRequiredService<MedalService>();
        if (!(await medals.GetSettingsAsync()).Enabled) return;
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var push = scope.ServiceProvider.GetRequiredService<IWebPushService>();

        List<(int Id, string UserId)> clients;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            clients = (await db.Clients.AsNoTracking()
                    .Where(c => c.Status == ClientStatus.Active && c.ApplicationUserId != null)
                    .Select(c => new { c.Id, c.ApplicationUserId }).ToListAsync(ct))
                .Select(c => (c.Id, c.ApplicationUserId!)).ToList();

        foreach (var (id, userId) in clients)
        {
            if (ct.IsCancellationRequested) break;
            await medals.SyncAsync(id);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var pending = await db.ClientMedals.Where(m => m.ClientId == id && !m.Notified && m.SeenAt == null).ToListAsync(ct);
            if (pending.Count == 0) continue;
            var first = MedalRules.Find(pending[0].Key);
            await push.SendAsync(userId, new PushMessageDto
            {
                Title = pending.Count == 1 && first is not null ? $"Nowy medal: {first.Name} 🏅" : $"Nowe medale: {pending.Count} 🏅",
                Body = pending.Count == 1 && first is not null ? first.Description : "Zajrzyj do gabloty — są tam nowe medale.",
                Url = "/medale",
                Category = NotificationTypes.PushMedals,
            });
            pending.ForEach(m => m.Notified = true);
            await db.SaveChangesAsync(ct);
        }
    }
}
