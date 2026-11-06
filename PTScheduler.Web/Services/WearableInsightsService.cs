using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Podsumowania z zegarka dla klienta: powiadomienie po treningu dopiętym do wizyty i poniedziałkowy raport tygodnia.
/// Oba włącza studio (Ustawienia → Moduły), a klient może je wyłączyć u siebie (Zegarek Garmin). Same liczby z reguł — bez AI.
/// </summary>
public class WearableInsightsService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IModuleSettingsService modules,
    IWebPushService push,
    EntitlementService entitlements,
    IAppClock clock,
    ILogger<WearableInsightsService> logger)
{
    public const string Url = "/my/garmin";

    public sealed record SessionActivity(WearableActivity Activity, DateTime SessionStart, string SessionType, string Line);

    public async Task<(bool Sessions, bool Weekly)> EnabledAsync()
    {
        if (!entitlements.IsAllowed("Wearables")) return (false, false);
        var m = await modules.GetAsync();
        return (m.WearableSessionSummaryEnabled, m.WearableWeeklyReportEnabled);
    }

    /// <summary>Po nowych danych: treningi dopięte do wizyty, o których klient jeszcze nie wie → jedno powiadomienie na trening.</summary>
    public async Task<int> NotifySessionSummariesAsync(int clientId)
    {
        if (!(await EnabledAsync()).Sessions) return 0;
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = await db.WearableLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == WearableService.Garmin);
        if (link is not { IsConnected: true, NotifySessions: true }) return 0;
        var userId = await db.Clients.Where(c => c.Id == clientId).Select(c => c.ApplicationUserId).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(userId)) return 0;

        // Tylko świeże treningi — zaległe paczki (np. po przerwie) nie zasypią klienta powiadomieniami.
        var since = DateTime.UtcNow.AddDays(-2);
        var acts = await db.WearableActivities
            .Where(a => a.ClientId == clientId && a.SessionId != null && a.SummarySentAt == null && a.StartUtc >= since)
            .OrderBy(a => a.StartUtc).ToListAsync();
        foreach (var a in acts) a.SummarySentAt = DateTime.UtcNow; // najpierw znacznik — awaria wysyłki nie powoduje powtórek
        await db.SaveChangesAsync();
        foreach (var a in acts)
        {
            try
            {
                await push.SendAsync(userId, new PushMessageDto
                {
                    Title = $"Twój trening · {WearableService.Attribution(a.DeviceName ?? link.DeviceName)}",
                    Body = WearableWeekRules.SessionLine(a.DurationSeconds, a.Kilocalories, a.AverageHeartRate, a.MaxHeartRate, a.DistanceMeters),
                    Url = Url
                });
            }
            catch (Exception ex) { logger.LogWarning(ex, "Podsumowanie treningu z zegarka nie wyszło (klient {Client}).", clientId); }
        }
        return acts.Count;
    }

    /// <summary>Raport tygodnia (domyślnie ostatni pełny tydzień) z porównaniem do poprzedniego.</summary>
    public async Task<WearableWeekRules.Week> WeekAsync(int clientId, DateOnly? from = null)
    {
        var start = from ?? WearableWeekRules.LastFullWeekStart(clock.Today);
        await using var db = await dbFactory.CreateDbContextAsync();
        var prev = start.AddDays(-7);
        var end = start.AddDays(7);
        var days = await db.WearableDays.AsNoTracking().Where(d => d.ClientId == clientId && d.Date >= prev && d.Date < end).ToListAsync();
        // Aktywności ładujemy z zapasem doby (strefa czasowa) i przypisujemy do dnia lokalnego.
        var fromUtc = start.ToDateTime(TimeOnly.MinValue).AddDays(-1);
        var toUtc = end.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var acts = (await db.WearableActivities.AsNoTracking()
                .Where(a => a.ClientId == clientId && a.StartUtc >= fromUtc && a.StartUtc < toUtc)
                .Select(a => new { a.StartUtc, a.DurationSeconds, a.SessionId }).ToListAsync())
            .Select(a => new WearableWeekRules.Act(DateOnly.FromDateTime(clock.ToWallClock(a.StartUtc)), a.DurationSeconds, a.SessionId != null))
            .ToList();
        return WearableWeekRules.For(start, days, acts);
    }

    /// <summary>Ostatnie treningi z zegarka dopięte do wizyt — lista u klienta.</summary>
    public async Task<List<SessionActivity>> SessionActivitiesAsync(int clientId, int take = 5)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await (from a in db.WearableActivities.AsNoTracking()
                          join s in db.Sessions.AsNoTracking() on a.SessionId equals s.Id
                          where a.ClientId == clientId
                          orderby a.StartUtc descending
                          select new { a, s.StartTime, Type = s.SessionType.Name })
            .Take(take).ToListAsync();
        return rows.Select(r => new SessionActivity(r.a, r.StartTime, r.Type,
            WearableWeekRules.SessionLine(r.a.DurationSeconds, r.a.Kilocalories, r.a.AverageHeartRate, r.a.MaxHeartRate, r.a.DistanceMeters))).ToList();
    }

    public async Task SetClientNotificationsAsync(int clientId, bool sessions, bool weekly)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = await db.WearableLinks.FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == WearableService.Garmin);
        if (link is null) return;
        link.NotifySessions = sessions;
        link.NotifyWeekly = weekly;
        await db.SaveChangesAsync();
    }

    /// <summary>Poniedziałek: raport poprzedniego tygodnia dla każdego klienta z połączonym zegarkiem (raz na tydzień).</summary>
    public async Task<int> SendWeeklyReportsAsync(CancellationToken ct = default)
    {
        if (!(await EnabledAsync()).Weekly) return 0;
        var week = WearableWeekRules.LastFullWeekStart(clock.Today);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var links = await db.WearableLinks
            .Where(l => l.Provider == WearableService.Garmin && l.ConnectedAt != null && l.DisconnectedAt == null && l.NotifyWeekly
                        && (l.WeeklyReportSentFor == null || l.WeeklyReportSentFor != week))
            .ToListAsync(ct);
        var sent = 0;
        foreach (var link in links)
        {
            if (ct.IsCancellationRequested) break;
            link.WeeklyReportSentFor = week;
            await db.SaveChangesAsync(ct);
            var report = await WeekAsync(link.ClientId, week);
            if (!report.HasData) continue;
            var userId = await db.Clients.Where(c => c.Id == link.ClientId).Select(c => c.ApplicationUserId).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(userId)) continue;
            try
            {
                await push.SendAsync(userId, new PushMessageDto { Title = $"Twój tydzień · {WearableService.Attribution(link.DeviceName)}", Body = WearableWeekRules.Text(report), Url = Url });
                sent++;
            }
            catch (Exception ex) { logger.LogWarning(ex, "Raport tygodnia z zegarka nie wyszedł (klient {Client}).", link.ClientId); }
        }
        return sent;
    }
}

/// <summary>Co godzinę sprawdza, czy jest poniedziałek (8–20) — wtedy wysyła raporty tygodnia z zegarka.</summary>
public class WearableWeeklyReportBackgroundService(IServiceScopeFactory scopes, ILogger<WearableWeeklyReportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(6), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var now = scope.ServiceProvider.GetRequiredService<IAppClock>().LocalNow;
                if (now.DayOfWeek == DayOfWeek.Monday && now.Hour is >= 8 and <= 20)
                    await scope.ServiceProvider.GetRequiredService<WearableInsightsService>().SendWeeklyReportsAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Raporty tygodnia z zegarka się nie udały."); }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
