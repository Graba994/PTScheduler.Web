using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Karta „przed treningiem”: wszystko, co trener chce wiedzieć 15 minut przed wizytą — który to trening,
/// uwagi z ankiety zdrowotnej, samopoczucie po ostatnim treningu, co klient wtedy zrobił, proponowany dzień
/// planu na dziś, propozycje progresji i wnioski, notatki i płatności. Same dane z aplikacji.
/// </summary>
public class PreSessionBriefService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IProgressionService progression,
    IClientProgressService progress,
    PlatformAssistantService assistant,
    EntitlementService entitlements,
    IAppClock clock)
{
    public sealed record ExerciseLine(string Name, string Detail);
    public sealed record Card(
        int SessionId, int ClientId, string ClientName, string? Phone, DateTime Start, string Type, int Number,
        string? PackageInfo, bool UnpaidAtTrainer,
        List<string> HealthNotes, DateOnly? HealthDate,
        List<string> LastFeeling, string? LastComment, DateOnly? LastFeelingDate,
        DateOnly? LastWorkoutDate, List<ExerciseLine> LastWorkout,
        string? PlanName, string? PlanDayLabel, int? PlanId, List<ExerciseLine> PlanToday,
        List<ProgressionSuggestionDto> Progression, List<PlanInsightDto> Insights,
        List<(DateTime At, string Text)> Notes, string Summary, string? Mascot, string? MascotName,
        int? PackageLeft = null, bool SummaryFromAi = false, int? PlanDayId = null,
        PTScheduler.Domain.Rules.ReadinessRules.Result? Readiness = null, string? Device = null);

    /// <summary>Karta dla treningu — null, gdy go nie ma albo nie należy do tego trenera.</summary>
    public async Task<Card?> GetAsync(int sessionId, string userId, bool isAdmin)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var s = await db.Sessions.AsNoTracking()
            .Where(x => x.Id == sessionId && (isAdmin || x.TrainerUserId == userId))
            .Select(x => new
            {
                x.Id, x.ClientId, x.StartTime, x.OffPackagePayment, x.PaidAt, x.TrainerUserId,
                Type = x.SessionType.Name,
                Client = x.Client.FirstName + " " + x.Client.LastName,
                FirstName = x.Client.FirstName,
                x.Client.Phone,
                Package = x.Package != null ? new { x.Package.Name, Left = x.Package.TotalSessions - x.Package.UsedSessions, x.Package.TotalSessions } : null
            })
            .FirstOrDefaultAsync();
        if (s is null) return null;

        var done = await db.Sessions.CountAsync(x => x.ClientId == s.ClientId && x.Status == SessionStatus.Completed && x.StartTime < s.StartTime);

        // Ankieta zdrowotna: odpowiedzi oznaczone do uwagi + urazy opisane słowami.
        var health = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == s.ClientId && r.Kind == SurveyKind.HealthIntake)
            .OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync();
        var healthNotes = new List<string>();
        if (health is not null)
            foreach (var a in SurveyJson.Deserialize(health.AnswersJson, new List<SurveyAnswer>()))
                if ((a.Flagged || a.Key is "injuries" or "medications") && !a.IsEmpty && !(a.Type == SurveyQuestionType.YesNo && a.Bool == false))
                    healthNotes.Add(a.Type == SurveyQuestionType.YesNo ? a.Label : $"{a.Label}: {a.Display}");

        // Samopoczucie z ostatniej ankiety po treningu.
        var post = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == s.ClientId && r.Kind == SurveyKind.PostWorkout)
            .OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync();
        var feeling = new List<string>();
        string? comment = null;
        if (post is not null)
        {
            var answers = SurveyJson.Deserialize(post.AnswersJson, new List<SurveyAnswer>());
            foreach (var key in new[] { SurveyKeys.Rpe, SurveyKeys.Energy, SurveyKeys.Sleep, SurveyKeys.Soreness, SurveyKeys.Pain, SurveyKeys.PainLocation })
                if (answers.FirstOrDefault(a => a.Key == key) is { IsEmpty: false } a && !(a.Type == SurveyQuestionType.YesNo && a.Bool == false))
                    feeling.Add($"{a.Label}: {a.Display}");
            comment = answers.FirstOrDefault(a => a.Key == SurveyKeys.Comment)?.Text;
        }

        // Ostatni zapisany trening.
        var lastDate = await db.WorkoutLogs.Where(l => l.ClientId == s.ClientId).MaxAsync(l => (DateOnly?)l.WorkoutDate);
        var lastLogs = lastDate is null ? [] : await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.ClientId == s.ClientId && l.WorkoutDate == lastDate)
            .Select(l => new { l.Exercise!.NamePl, l.PlanExerciseId, PlanDayId = l.PlanExercise != null ? (int?)l.PlanExercise.PlanDayId : null,
                Sets = l.Sets.OrderBy(x => x.SetNumber).Select(x => new { x.Reps, x.WeightKg }).ToList() })
            .ToListAsync();
        var lastWorkout = lastLogs.Select(l => new ExerciseLine(l.NamePl,
            string.Join(" · ", l.Sets.Select(x => x.WeightKg > 0 ? $"{x.Reps}×{x.WeightKg:0.##}" : $"{x.Reps}")))).ToList();

        // Dzień planu: wybrany przez trenera na tę wizytę, a bez wyboru — następny po ostatnio zrobionym
        // w pierwszym aktywnym planie klienta (kolejność planów ustala trener).
        var chosenDayId = await db.Sessions.Where(x => x.Id == s.Id).Select(x => x.PlanDayId).FirstOrDefaultAsync();
        var chosenPlanId = chosenDayId is null ? null
            : await db.PlanDays.Where(d => d.Id == chosenDayId).Select(d => (int?)d.PlanId).FirstOrDefaultAsync();
        var plan = await db.TrainingPlans.AsNoTracking()
            .Where(p => p.ClientId == s.ClientId && !p.IsTemplate && (chosenPlanId != null ? p.Id == chosenPlanId : !p.IsArchived && p.Use != PlanUse.Solo))
            .OrderBy(p => p.SortOrder).ThenByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id, p.Name,
                Days = p.Days.OrderBy(d => d.Order).Select(d => new
                {
                    d.Id, d.Label,
                    Exercises = d.Exercises.OrderBy(e => e.Order).Select(e => new { e.Exercise!.NamePl, e.Sets, e.Reps, e.TargetWeightKg }).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync();
        string? dayLabel = null;
        int? dayId = null;
        var planToday = new List<ExerciseLine>();
        if (plan is { Days.Count: > 0 })
        {
            var lastDayId = lastLogs.Select(l => l.PlanDayId).FirstOrDefault(id => id != null);
            var idx = plan.Days.FindIndex(d => d.Id == lastDayId);
            var day = plan.Days.FirstOrDefault(d => d.Id == chosenDayId) ?? plan.Days[(idx + 1) % plan.Days.Count];
            dayLabel = string.IsNullOrWhiteSpace(day.Label) ? $"Dzień {plan.Days.IndexOf(day) + 1}" : day.Label;
            dayId = day.Id;
            planToday = day.Exercises.Select(e => new ExerciseLine(e.NamePl,
                $"{e.Sets} × {e.Reps ?? "—"}" + (e.TargetWeightKg is decimal kg and > 0 ? $" · {kg:0.##} kg" : ""))).ToList();
        }

        var suggestions = await progression.GetSuggestionsAsync(userId, isAdmin, s.ClientId);
        var insights = (await progress.GetAsync(s.ClientId)).Insights.Take(3).ToList();
        var notes = await db.TrainerNotes.AsNoTracking()
            .Where(n => n.ClientId == s.ClientId)
            .OrderByDescending(n => n.CreatedAt).Take(2)
            .Select(n => new { n.CreatedAt, n.Content }).ToListAsync();

        // Gotowość z zegarka (Garmin) — tylko dla wizyty dziś i tylko, gdy plan i Portal na to pozwalają.
        PTScheduler.Domain.Rules.ReadinessRules.Result? readiness = null;
        string? device = null;
        var wearableAi = false;
        if (entitlements.IsAllowed("Wearables") && DateOnly.FromDateTime(s.StartTime) == clock.Today)
        {
            readiness = (await WearableService.ReadinessAsync(db, clock.Today, [s.ClientId])).GetValueOrDefault(s.ClientId);
            if (readiness is not null)
            {
                var link = await db.WearableLinks.AsNoTracking().Where(l => l.ClientId == s.ClientId)
                    .Select(l => new { l.DeviceName, l.AiConsentAt }).FirstOrDefaultAsync();
                device = link?.DeviceName;
                wearableAi = link?.AiConsentAt is not null;
            }
        }

        var number = done + 1;
        var parts = new List<string> { number == 1 ? "[klient] — pierwszy trening. Warto zacząć od rozmowy i testów." : $"[klient] — {number}. trening." };
        if (readiness is { Level: PTScheduler.Domain.Rules.ReadinessRules.Level.Low })
            parts.Add($"Zegarek: gotowość niska ({string.Join(", ", readiness.Reasons.Take(2))}) — rozważ lżejszy trening.");
        else if (readiness is { Level: PTScheduler.Domain.Rules.ReadinessRules.Level.Good })
            parts.Add("Zegarek: dobra forma — to dzień na mocniejszy trening.");
        if (healthNotes.Count > 0) parts.Add($"W ankiecie zdrowotnej są uwagi ({healthNotes.Count}) — rzuć okiem poniżej.");
        if (suggestions.Count > 0) parts.Add($"{suggestions.Count} {AssistantTipsService.Plural(suggestions.Count, "propozycja zmiany ciężaru", "propozycje zmiany ciężaru", "propozycji zmiany ciężaru")} na dziś.");
        if (s.OffPackagePayment == "trainer" && s.PaidAt is null) parts.Add("Płatność za ten trening u Ciebie.");
        var fallback = string.Join(" ", parts);
        var facts = new Dictionary<string, string>
        {
            ["klient"] = "[klient]",
            ["numer_treningu"] = number.ToString(),
            ["uwagi_zdrowotne"] = healthNotes.Count.ToString(),
            ["propozycje_progresji"] = suggestions.Count.ToString(),
            ["platnosc_u_trenera"] = s.OffPackagePayment == "trainer" && s.PaidAt is null ? "tak" : "nie",
        };
        // Do AI tylko poziom i powody z liczbami — bez imienia (to już [klient]) i bez danych kontaktowych.
        if (readiness is not null)
        {
            facts["gotowosc_z_zegarka"] = PTScheduler.Domain.Rules.ReadinessRules.Label(readiness.Level);
            facts["powody_gotowosci"] = string.Join("; ", readiness.Reasons);
        }
        var config = await assistant.GetConfigAsync();
        // Dane z zegarka idą do AI tylko za osobną zgodą klienta (wymóg Garmina). Bez niej — podsumowanie z reguł, bez modelu
        // (tekst zapasowy też trafiłby do modelu, a jest w nim linijka o gotowości).
        var useAi = config?.Ai == true && (readiness is null || wearableAi);
        var rawSummary = await assistant.PhraseAsync($"before-{s.Id}", "przed_treningiem", facts, fallback, useAi);
        var summary = rawSummary.Replace("[klient]", string.IsNullOrWhiteSpace(s.FirstName) ? "Klient" : s.FirstName.Trim());

        return new Card(s.Id, s.ClientId, s.Client.Trim(), s.Phone, s.StartTime, s.Type, number,
            s.Package is { } pk ? $"{pk.Name}: zostało {pk.Left} z {pk.TotalSessions}" : null,
            s.OffPackagePayment == "trainer" && s.PaidAt is null,
            healthNotes, health is null ? null : DateOnly.FromDateTime(health.SubmittedAt),
            feeling, comment, post?.WorkoutDate ?? (post is null ? null : DateOnly.FromDateTime(post.SubmittedAt)),
            lastDate, lastWorkout,
            plan?.Name, dayLabel, plan?.Id, planToday,
            suggestions, insights,
            notes.Select(n => (n.CreatedAt, n.Content)).ToList(),
            summary,
            config is { Enabled: true } ? config.Icons.GetValueOrDefault(healthNotes.Count > 0 ? "think" : "hello") : null,
            config is { Enabled: true } ? config.Name : null,
            s.Package?.Left, config?.Ai == true && rawSummary != fallback, dayId, readiness, device);
    }

}

/// <summary>Co 2 minuty: treningi zaczynające się za ~15 minut → push do trenera z kartą „przed treningiem”.</summary>
public class BeforeSessionPushService(IServiceScopeFactory scopeFactory, ILogger<BeforeSessionPushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await RunAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Powiadomienia przed treningiem nie powiodły się."); }
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
        }
    }

    private async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var push = scope.ServiceProvider.GetRequiredService<IWebPushService>();
        if (!(await push.GetSettingsAsync()).IsConfigured) return;
        var clock = scope.ServiceProvider.GetRequiredService<IAppClock>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.LocalNow;
        var due = await db.Sessions
            .Where(s => s.Status == SessionStatus.Scheduled && s.TrainerBriefSentAt == null
                && s.StartTime > now.AddMinutes(5) && s.StartTime <= now.AddMinutes(16))
            .Select(s => new { Session = s, Client = s.Client.FirstName + " " + s.Client.LastName,
                Done = db.Sessions.Count(x => x.ClientId == s.ClientId && x.Status == SessionStatus.Completed) })
            .ToListAsync();
        var ready = due.Count > 0 && scope.ServiceProvider.GetRequiredService<EntitlementService>().IsAllowed("Wearables")
            ? await WearableService.ReadinessAsync(db, clock.Today, due.Select(d => d.Session.ClientId).Distinct().ToList())
            : [];
        foreach (var d in due)
        {
            d.Session.TrainerBriefSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            var minutes = Math.Max(1, (int)Math.Round((d.Session.StartTime - now).TotalMinutes));
            var body = d.Done == 0 ? "Pierwszy trening — zobacz kartę przed treningiem." : $"{d.Done + 1}. trening — zobacz kartę przed treningiem.";
            if (ready.TryGetValue(d.Session.ClientId, out var r)) body = $"{WearableService.Line(r, 1)}. {body}";
            await push.SendAsync(d.Session.TrainerUserId, new PushMessageDto
            {
                Title = $"Za {minutes} min: {d.Client.Trim()}",
                Body = body,
                Url = $"/clients/{d.Session.ClientId}?trening={d.Session.Id}#trening",
                Category = NotificationTypes.PushBeforeSession
            });
        }
    }
}
