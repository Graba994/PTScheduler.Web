using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Retencja klientów trenera: ryzyko odejścia (ClientChurnRules), trend samopoczucia z ankiet po treningu
/// (WellbeingTrend) i zestawienie ankiety zdrowotnej z planem (HealthPlanRules). Tylko reguły — bez AI.
/// </summary>
public class RetentionService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock)
{
    public sealed record ClientRisk(int ClientId, string Name, string FirstName, string? Phone,
        ClientChurnRules.Result Result, WellbeingSummary Wellbeing, int CompletedTotal, int? PackageRemaining, int? DaysSinceLastVisit);

    public sealed record ClientInsights(WellbeingSummary Wellbeing, List<HealthPlanRules.Flag> HealthFlags, ClientChurnRules.Result Risk);

    /// <summary>Klienci z ryzykiem co najmniej średnim, od najbardziej zagrożonych.</summary>
    public async Task<List<ClientRisk>> GetRisksAsync(string? trainerUserId)
    {
        var all = await ComputeAsync(trainerUserId, null);
        return all.Where(r => r.Result.IsRisk).OrderByDescending(r => r.Result.Score).ThenBy(r => r.Name).ToList();
    }

    public async Task<ClientInsights?> GetInsightsAsync(int clientId)
    {
        var risk = (await ComputeAsync(null, clientId)).FirstOrDefault();
        if (risk is null) return null;
        return new(risk.Wellbeing, await HealthFlagsAsync(clientId), risk.Result);
    }

    private async Task<List<ClientRisk>> ComputeAsync(string? trainerUserId, int? onlyClientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.Status == ClientStatus.Active && (trainerUserId == null || c.TrainerUserId == trainerUserId)
                     && (onlyClientId == null || c.Id == onlyClientId))
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Phone })
            .ToListAsync();
        if (clients.Count == 0) return [];
        var ids = clients.Select(c => c.Id).ToList();

        // Wizyty: StartTime to zegar ścienny → LocalNow.
        var now = clock.LocalNow;
        var from = now.AddDays(-90);
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => ids.Contains(s.ClientId) && (s.StartTime >= from || s.Status == SessionStatus.Completed))
            .Select(s => new { s.ClientId, s.StartTime, s.Status })
            .ToListAsync();
        var byClient = sessions.GroupBy(s => s.ClientId).ToDictionary(g => g.Key, g => g.ToList());

        var utcNow = clock.UtcNow;
        var packages = (await db.SessionPackages.AsNoTracking()
                .Where(p => ids.Contains(p.ClientId) && p.Status != PackageStatus.Cancelled && !p.IsHidden)
                .Select(p => new { p.ClientId, p.Status, p.TotalSessions, p.UsedSessions, p.ExpiresAt, p.PurchasedAt })
                .ToListAsync())
            .GroupBy(p => p.ClientId).ToDictionary(g => g.Key, g => g.ToList());

        var surveyFrom = utcNow.AddDays(-120);
        var surveys = (await db.SurveyResponses.AsNoTracking()
                .Where(r => ids.Contains(r.ClientId) && r.Kind == SurveyKind.PostWorkout && r.SubmittedAt >= surveyFrom)
                .Select(r => new { r.Id, r.ClientId, r.WorkoutDate, r.SubmittedAt, r.AnswersJson })
                .ToListAsync())
            .GroupBy(r => r.ClientId)
            .ToDictionary(g => g.Key, g => g.Select(r => new SurveyResponseDto
            {
                Id = r.Id, ClientId = r.ClientId, Kind = SurveyKind.PostWorkout, WorkoutDate = r.WorkoutDate ?? DateOnly.FromDateTime(r.SubmittedAt),
                SubmittedAtUtc = r.SubmittedAt, Answers = SurveyJson.Deserialize(r.AnswersJson, new List<SurveyAnswer>())
            }).ToList());

        var today = clock.Today;
        var workoutFrom = today.AddDays(-42);
        var workouts = (await db.WorkoutLogs.AsNoTracking()
                .Where(w => ids.Contains(w.ClientId) && w.WorkoutDate >= workoutFrom)
                .Select(w => new { w.ClientId, w.WorkoutDate }).Distinct().ToListAsync())
            .GroupBy(w => w.ClientId).ToDictionary(g => g.Key, g => g.Select(w => w.WorkoutDate).ToList());

        var result = new List<ClientRisk>();
        foreach (var c in clients)
        {
            var list = byClient.GetValueOrDefault(c.Id) ?? [];
            var visited = list.Where(s => s.StartTime < now && s.Status is SessionStatus.Completed or SessionStatus.Scheduled).ToList();
            int? daysSince = visited.Count > 0 ? (now.Date - visited.Max(s => s.StartTime).Date).Days : null;
            var upcoming = list.Any(s => s.StartTime >= now && s.Status is SessionStatus.Scheduled or SessionStatus.AwaitingPackage);
            var last28 = visited.Count(s => s.StartTime >= now.AddDays(-28));
            var prev56 = visited.Count(s => s.StartTime >= now.AddDays(-84) && s.StartTime < now.AddDays(-28));
            var completedTotal = list.Count(s => s.Status == SessionStatus.Completed);
            var cancelled = list.Count(s => s.Status == SessionStatus.Cancelled && s.StartTime >= now.AddDays(-30) && s.StartTime < now);
            var noShows = list.Count(s => s.Status == SessionStatus.NoShow && s.StartTime >= now.AddDays(-30));

            int? remaining = null;
            var endedRecently = false;
            if (packages.TryGetValue(c.Id, out var pkgs))
            {
                var active = pkgs.Where(p => p.Status == PackageStatus.Active && (p.ExpiresAt == null || p.ExpiresAt > utcNow)).ToList();
                remaining = active.Sum(p => Math.Max(0, p.TotalSessions - p.UsedSessions));
                if (remaining == 0)
                {
                    var lastEnded = pkgs.Max(p => p.ExpiresAt is { } e && e < utcNow ? e : p.PurchasedAt);
                    endedRecently = (utcNow - lastEnded).TotalDays <= 60;
                }
            }

            var wellbeing = WellbeingTrend.Compute(surveys.GetValueOrDefault(c.Id) ?? []);
            var painFrom = today.AddDays(-14);
            var recentPain = (surveys.GetValueOrDefault(c.Id) ?? []).Any(r => r.WorkoutDate >= painFrom && r[SurveyKeys.Pain]?.Bool == true);
            var days = workouts.GetValueOrDefault(c.Id) ?? [];
            var app14 = days.Count(d => d > today.AddDays(-14));
            var app28 = days.Count(d => d <= today.AddDays(-14));

            var input = new ClientChurnRules.Input(daysSince, upcoming, last28, prev56, completedTotal, cancelled, noShows,
                remaining, endedRecently, wellbeing.IsDropping, wellbeing.LowSatisfaction, recentPain, app14, app28);
            result.Add(new(c.Id, $"{c.FirstName} {c.LastName}".Trim(), c.FirstName.Trim(), c.Phone,
                ClientChurnRules.Score(input), wellbeing, completedTotal, remaining, daysSince));
        }
        return result;
    }

    /// <summary>Ankieta zdrowotna i ostatnie zgłoszenia bólu po treningu zestawione z aktualnym planem klienta.</summary>
    public async Task<List<HealthPlanRules.Flag>> HealthFlagsAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var planId = await db.TrainingPlans.AsNoTracking()
            .Where(p => p.ClientId == clientId && !p.IsTemplate)
            .OrderByDescending(p => p.CreatedAt).Select(p => (int?)p.Id).FirstOrDefaultAsync();
        if (planId is null) return [];
        var exercises = (await db.PlanExercises.AsNoTracking()
                .Where(e => e.PlanDay!.PlanId == planId)
                .Select(e => new { e.Exercise!.NamePl, e.Exercise.PrimaryMuscles, e.Exercise.SecondaryMuscles, e.Exercise.Category })
                .ToListAsync())
            .Select(e => new HealthPlanRules.PlanExercise(e.NamePl, e.PrimaryMuscles, e.SecondaryMuscles, e.Category)).ToList();
        if (exercises.Count == 0) return [];

        var notes = new List<HealthPlanRules.Note>();
        var pregnancy = false;
        var heart = false;
        var health = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == clientId && r.Kind == SurveyKind.HealthIntake)
            .OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync();
        if (health is not null)
        {
            var answers = SurveyJson.Deserialize(health.AnswersJson, new List<SurveyAnswer>());
            foreach (var key in new[] { "injuries", "other_health" })
                if (answers.FirstOrDefault(a => a.Key == key)?.Text is { Length: > 0 } text)
                    notes.Add(new("Ankieta zdrowotna", text));
            pregnancy = answers.FirstOrDefault(a => a.Key == "pregnancy")?.Bool == true;
            heart = answers.FirstOrDefault(a => a.Key == "parq_heart")?.Bool == true;
        }

        var since = clock.UtcNow.AddDays(-30);
        var posts = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == clientId && r.Kind == SurveyKind.PostWorkout && r.SubmittedAt >= since && r.FlagCount > 0)
            .OrderByDescending(r => r.SubmittedAt).Take(10)
            .Select(r => new { r.WorkoutDate, r.SubmittedAt, r.AnswersJson }).ToListAsync();
        foreach (var p in posts)
        {
            var answers = SurveyJson.Deserialize(p.AnswersJson, new List<SurveyAnswer>());
            if (answers.FirstOrDefault(a => a.Key == SurveyKeys.PainLocation)?.Text is { Length: > 0 } where)
                notes.Add(new($"Po treningu {(p.WorkoutDate ?? DateOnly.FromDateTime(p.SubmittedAt)):dd.MM}", where));
        }
        return HealthPlanRules.Check(notes, pregnancy, heart, exercises);
    }
}
