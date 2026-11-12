using System.Net;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Medals;
using PTScheduler.Application.Progress;
using PTScheduler.Application.Training;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Twój miesiąc” / „Twój rok” dla klienta: liczy podsumowanie z historii (treningi, seria tygodni, rekordy,
/// największy postęp, medale), prosi asystenta o ładny tekst (pod marką trenera) i wysyła je 1. dnia miesiąca
/// e-mailem i pushem — zgodnie z ustawieniami klienta i włączonymi podsumowaniami dla klientów.
/// </summary>
public class ClientRecapService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    PlatformAssistantService assistant,
    IModuleSettingsService modules,
    ISiteContentService site,
    IEmailService email,
    IWebPushService push,
    INotificationPreferencesService prefs,
    IBrandingService branding,
    IAppClock clock,
    ILogger<ClientRecapService> logger)
{
    public sealed record Recap(RecapResult Result, string Text, bool IsYear, DateOnly From);

    /// <summary>Podsumowanie, które dziś wisi na pulpicie klienta (albo null — poza oknem, bez treningów, wyłączone).</summary>
    /// <param name="anyDay">Podgląd trenera: ostatni pełny miesiąc (w styczniu — rok) niezależnie od dnia.</param>
    public async Task<Recap?> CurrentAsync(int clientId, bool anyDay = false)
    {
        var today = clock.Today;
        if ((!anyDay && !ClientRecap.IsVisible(today)) || !(await modules.GetAsync()).ClientProgressSummariesEnabled) return null;
        var (isYear, from, to) = ClientRecap.PeriodFor(today);
        return await BuildAsync(clientId, isYear, from, to);
    }

    public async Task<Recap?> BuildAsync(int clientId, bool isYear, DateOnly from, DateOnly to)
    {
        var prevFrom = isYear ? from.AddYears(-1) : from.AddMonths(-1);
        await using var db = await dbFactory.CreateDbContextAsync();

        // Dni z treningiem: odbyte wizyty i zapisy w dzienniku (jak w medalach) — od początku poprzedniego okresu.
        var loadFrom = prevFrom.AddDays(-7 * 60);
        var fromTime = loadFrom.ToDateTime(TimeOnly.MinValue);
        var toTime = to.ToDateTime(TimeOnly.MinValue);
        var sessionDays = (await db.Sessions.AsNoTracking()
                .Where(s => s.ClientId == clientId && s.Status == SessionStatus.Completed && s.StartTime >= fromTime && s.StartTime < toTime)
                .Select(s => s.StartTime).ToListAsync())
            .Select(DateOnly.FromDateTime);
        var workoutDays = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.ClientId == clientId && l.WorkoutDate >= loadFrom && l.WorkoutDate < to)
            .Select(l => l.WorkoutDate).Distinct().ToListAsync();
        var days = sessionDays.Concat(workoutDays).Distinct().ToList();
        var inPeriod = days.Where(d => d >= from && d < to).ToList();
        if (inPeriod.Count == 0) return null;

        // Najlepszy ciężar w ćwiczeniu na dany dzień — cała historia do końca okresu (rekord = ciężej niż kiedykolwiek wcześniej).
        var perExercise = (await db.WorkoutSetLogs.AsNoTracking()
                .Where(s => s.WorkoutLog!.ClientId == clientId && s.WorkoutLog.WorkoutDate < to && s.Reps > 0 && s.WeightKg > 0)
                .GroupBy(s => new { s.WorkoutLog!.ExerciseId, s.WorkoutLog.WorkoutDate })
                .Select(g => new { g.Key.ExerciseId, g.Key.WorkoutDate, Max = g.Max(s => s.WeightKg) })
                .ToListAsync())
            .Select(x => (x.ExerciseId, x.WorkoutDate, x.Max)).ToList();
        var (records, topRecord) = ClientRecap.RecordsIn(perExercise, from, to);
        var gain = isYear ? ClientRecap.TopGain(perExercise, from, to) : null;
        var exerciseIds = new[] { topRecord?.ExerciseId, gain?.ExerciseId }.Where(i => i is not null).Select(i => i!.Value).ToList();
        var names = await db.Exercises.AsNoTracking().Where(e => exerciseIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NamePl);

        // Medale (gdy trener je włączył) zdobyte w okresie — EarnedAt to instant, granice okresu to zegar ścienny.
        var medals = new List<string>();
        if ((await site.GetAsync()).MedalsEnabled)
        {
            var fromUtc = clock.ToUtc(from.ToDateTime(TimeOnly.MinValue));
            var toUtc = clock.ToUtc(toTime);
            var medalKeys = await db.ClientMedals.AsNoTracking()
                .Where(m => m.ClientId == clientId && m.EarnedAt >= fromUtc && m.EarnedAt < toUtc)
                .OrderBy(m => m.EarnedAt).Select(m => m.Key).ToListAsync();
            medals = medalKeys.Select(MedalRules.Find).Where(m => m is not null).Select(m => m!.Name).ToList();
        }

        var input = new RecapInput(
            isYear, from,
            TrainingDays: inPeriod.Count,
            TrainingDaysPrevious: days.Count(d => d >= prevFrom && d < from),
            WeekStreak: WorkoutHighlights.WeekStreak(days, to.AddDays(-1)),
            BestStreak: MedalRules.BestWeekStreak(inPeriod),
            ActiveMonths: inPeriod.Select(d => d.Month).Distinct().Count(),
            Records: records,
            TopRecord: topRecord is { } r && names.TryGetValue(r.ExerciseId, out var rn) ? (rn, r.Kg) : null,
            TopGain: gain is { } g && names.TryGetValue(g.ExerciseId, out var gn) ? (gn, g.FromKg, g.ToKg) : null,
            Medals: medals);
        var result = ClientRecap.Compute(input);
        if (!result.HasData) return null;

        var config = await assistant.GetConfigAsync();
        var text = await assistant.PhraseAsync($"recap-{clientId}-{from:yyyyMM}{(isYear ? "y" : "m")}", isYear ? "twoj_rok" : "twoj_miesiac",
            result.Facts, result.Fallback, config?.Ai == true, audience: "klient");
        return new(result, text, isYear, from);
    }

    /// <summary>Wysyła podsumowanie jednemu klientowi (e-mail i/lub push wg ustawień). Zwraca, czy coś poszło.</summary>
    public async Task<bool> SendAsync(int clientId, string userId, string? userEmail, string firstName, bool isYear, DateOnly from, DateOnly to)
    {
        var recap = await BuildAsync(clientId, isYear, from, to);
        if (recap is null) return false;
        var p = await prefs.GetAsync(userId);
        var sent = false;
        var brand = await branding.GetAsync();

        if (p.MonthlySummary && !string.IsNullOrWhiteSpace(userEmail) && await email.IsEnabledAsync())
        {
            try
            {
                await email.SendAsync(userEmail!, firstName, $"{recap.Result.Title} — {brand.CompanyName}", RenderHtml(recap, firstName, brand));
                sent = true;
            }
            catch (Exception ex) { logger.LogWarning(ex, "Nie udało się wysłać podsumowania miesiąca klientowi {Client}.", clientId); }
        }
        if (p.PushMonthlySummary && (await push.GetSettingsAsync()).IsConfigured && await push.GetSubscriptionCountAsync(userId) > 0)
        {
            await push.SendAsync(userId, new PushMessageDto
            {
                Title = $"{recap.Result.Title} 📊",
                Body = recap.Result.PushBody,
                Url = "/app",
                Category = NotificationTypes.PushMonthlySummary,
            });
            sent = true;
        }
        return sent;
    }

    /// <summary>E-mail pod marką trenera: kafelki z liczbami, tekst asystenta, najważniejsze momenty i przycisk do aplikacji.</summary>
    public static string RenderHtml(Recap recap, string firstName, AppBrandingDto brand)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var (primary, secondary) = MedalArt.BrandColors(brand.ThemeName);
        var appUrl = AppUrls.PublicBase() ?? "";
        var r = recap.Result;
        var greeting = firstName.Length > 0 ? $"Cześć, {E(firstName)}!" : "Cześć!";
        var tiles = string.Concat(r.Tiles.Take(4).Select(t => $"""
            <td align="center" style="padding:6px">
              <div style="background:#f6f7fb;border-radius:14px;padding:14px 6px">
                <div style="font-size:26px;font-weight:800;color:{primary};line-height:1">{E(t.Value)}</div>
                <div style="font-size:12px;color:#6b7280;margin-top:4px">{E(t.Label)}</div>
              </div>
            </td>
            """));
        var highlights = r.Highlights.Count == 0 ? "" : $"""
            <tr><td style="padding:18px 28px 0">
              <div style="font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:{primary};margin-bottom:6px">Najważniejsze</div>
              {string.Concat(r.Highlights.Select(h => $"<div style=\"padding:5px 0;color:#374151;font-size:14px\">⭐ {E(h)}</div>"))}
            </td></tr>
            """;
        var button = appUrl.Length == 0 ? "" : $"""
            <tr><td style="padding:24px 28px 6px">
              <a href="{E(appUrl + "/app")}" style="display:inline-block;padding:12px 22px;border-radius:12px;background:{primary};color:#ffffff;font-weight:700;font-size:15px;text-decoration:none">Zobacz w aplikacji</a>
            </td></tr>
            """;
        var settings = appUrl.Length == 0 ? "" : $"""<a href="{E(appUrl + "/Account/Manage/Notifications")}" style="color:#6b7280">zmień w ustawieniach powiadomień</a>""";
        return $"""
            <!DOCTYPE html>
            <html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(r.Title)}</title></head>
            <body style="margin:0;padding:0;background:#f4f5f9;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f5f9;padding:24px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 6px 24px rgba(15,23,42,.08)">
                    <tr><td style="background:linear-gradient(135deg,{primary},{secondary});background-color:{primary};padding:24px 28px">
                      <div style="color:rgba(255,255,255,.8);font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase">{E(brand.CompanyName)} · {E(r.Subtitle)}</div>
                      <div style="color:#ffffff;font-size:26px;font-weight:800;margin-top:6px">{E(r.Title)}</div>
                    </td></tr>
                    <tr><td style="padding:22px 28px 0;color:#111827;font-size:16px;font-weight:700">{greeting}</td></tr>
                    <tr><td style="padding:8px 28px 0;color:#1f2937;font-size:15px;line-height:1.55">{E(recap.Text)}</td></tr>
                    <tr><td style="padding:16px 22px 0">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0"><tr>{tiles}</tr></table>
                    </td></tr>
                    {highlights}
                    {button}
                    <tr><td style="padding:18px 28px 24px;color:#9ca3af;font-size:12px;line-height:1.5">
                      {E(brand.CompanyName)} · podsumowanie {(recap.IsYear ? "roku" : "miesiąca")}. Nie chcesz go dostawać? {settings}
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;
    }
}

/// <summary>Co godzinę 1. dnia miesiąca (9:00–20:00): podsumowanie poprzedniego miesiąca (w styczniu — roku) dla klientów z kontem.</summary>
public class ClientRecapBackgroundService(IServiceScopeFactory scopes, ILogger<ClientRecapBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Wysyłka podsumowań miesiąca się nie udała."); }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IAppClock>();
        var now = clock.LocalNow;
        if (now.Day != 1 || now.Hour < 9 || now.Hour > 20) return;
        if (!(await scope.ServiceProvider.GetRequiredService<IModuleSettingsService>().GetAsync()).ClientProgressSummariesEnabled) return;
        var recaps = scope.ServiceProvider.GetRequiredService<ClientRecapService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (isYear, from, to) = ClientRecap.PeriodFor(clock.Today);

        var clients = await (from c in db.Clients.AsNoTracking()
                             join u in db.Users.AsNoTracking() on c.ApplicationUserId equals u.Id
                             where c.Status == ClientStatus.Active
                             select new { c.Id, c.FirstName, UserId = u.Id, u.Email })
            .ToListAsync(ct);
        foreach (var c in clients)
        {
            if (ct.IsCancellationRequested) break;
            var p = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == c.UserId, ct);
            if (p is { MonthlySummary: false, PushMonthlySummary: false } || p?.MonthlySummarySentFor == from) continue;
            if (p is null) db.NotificationPreferences.Add(p = new NotificationPreferences { UserId = c.UserId });
            p.MonthlySummarySentFor = from; // najpierw znacznik — awaria wysyłki nie powoduje powtórek
            await db.SaveChangesAsync(ct);
            try { await recaps.SendAsync(c.Id, c.UserId, c.Email, c.FirstName.Trim(), isYear, from, to); }
            catch (Exception ex) { logger.LogWarning(ex, "Podsumowanie miesiąca dla klienta {Client} nie wyszło.", c.Id); }
        }
    }
}
