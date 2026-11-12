using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ClientProgressService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock) : IClientProgressService
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    /// <summary>Odstępstwa oceniamy tylko na ćwiczeniach robionych w ostatnich 6 tygodniach.</summary>
    private const int InsightLookbackDays = 42;

    public async Task<ClientProgressDto> GetAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = clock.LocalNow;

        var completed = db.Sessions.AsNoTracking().Where(s => s.ClientId == clientId && s.Status == SessionStatus.Completed);
        var completedCount = await completed.CountAsync();
        var firstSession = await completed.MinAsync(s => (DateTime?)s.StartTime);
        var totalMinutes = await completed.SumAsync(s => (int?)s.SessionType.DurationMinutes) ?? 0;
        var completedDates = await completed.Where(s => s.StartTime >= now.AddDays(-7 * 53)).Select(s => s.StartTime).ToListAsync();

        var monthAgo = now.AddDays(-30);
        var recent = await db.Sessions.AsNoTracking()
            .Where(s => s.ClientId == clientId && s.StartTime >= monthAgo && s.StartTime <= now)
            .Select(s => new { s.Status, s.IsLateCancellation })
            .ToListAsync();

        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.ClientId == clientId)
            .Select(l => new
            {
                l.ExerciseId,
                Name = l.Exercise!.NamePl,
                l.PlanExerciseId,
                l.WorkoutDate,
                Sets = l.Sets.OrderBy(s => s.SetNumber).Select(s => new SetResult(s.Reps, s.WeightKg)).ToList()
            })
            .ToListAsync();

        static List<ExerciseDay> Days(IEnumerable<(DateOnly Date, List<SetResult> Sets)> rows) =>
            rows.GroupBy(r => r.Date)
                .Select(g => new ExerciseDay(g.Key, g.SelectMany(x => x.Sets).ToList()))
                .OrderByDescending(d => d.Date)
                .ToList();

        var strength = logs.GroupBy(l => (l.ExerciseId, l.Name))
            .Select(g => (g.Key.Name, Change: ProgressInsights.StrengthChange(Days(g.Select(x => (x.WorkoutDate, x.Sets))))))
            .Where(x => x.Change is { } c && c.ToKg > c.FromKg)
            .Select(x => new StrengthHighlightDto(x.Name, x.Change!.Value.FromKg, x.Change.Value.ToKg))
            .OrderByDescending(s => s.DeltaKg).ThenBy(s => s.Exercise)
            .Take(4)
            .ToList();

        var measurements = await db.BodyMeasurements.AsNoTracking()
            .Where(m => m.ClientId == clientId)
            .OrderBy(m => m.MeasurementDate)
            .Select(m => new { m.WeightKg, m.WaistCm })
            .ToListAsync();
        var weights = measurements.Where(m => m.WeightKg != null).Select(m => m.WeightKg!.Value).ToList();
        var waists = measurements.Where(m => m.WaistCm != null).Select(m => m.WaistCm!.Value).ToList();

        // Odstępstwa od planu: ćwiczenia z aktualnych planów klienta, robione w ostatnich tygodniach.
        var since = DateOnly.FromDateTime(now).AddDays(-InsightLookbackDays);
        var planned = await db.PlanExercises.AsNoTracking()
            .Where(pe => pe.PlanDay!.Plan!.ClientId == clientId && !pe.PlanDay.Plan.IsTemplate)
            .Select(pe => new { pe.Id, pe.Sets, pe.Reps, pe.TargetWeightKg, pe.RestSeconds, pe.PlanDay!.PlanId, Name = pe.Exercise!.NamePl, DayOrder = pe.PlanDay.Order, pe.Order })
            .ToListAsync();
        var insights = new List<PlanInsightDto>();
        foreach (var pe in planned.OrderBy(p => p.DayOrder).ThenBy(p => p.Order))
        {
            var days = Days(logs.Where(l => l.PlanExerciseId == pe.Id).Select(l => (l.WorkoutDate, l.Sets)));
            if (days.Count == 0 || days[0].Date < since) continue;
            var found = ProgressInsights.Analyze(new PlannedTarget(pe.Sets, pe.Reps, pe.TargetWeightKg, pe.RestSeconds), days);
            if (found.Count == 0) continue;
            var plannedText = $"{pe.Sets} × {pe.Reps ?? "—"}" + (pe.TargetWeightKg is decimal kg and > 0 ? $" · {ProgressInsights.Kg(kg)}" : "");
            var lastText = ProgressInsights.RepsText(days[0]) + (days[0].TopKg > 0 ? $" · {ProgressInsights.Kg(days[0].TopKg)}" : "");
            insights.AddRange(found.Select(f => new PlanInsightDto(pe.Name, pe.PlanId, plannedText, lastText, f.Kind, f.Finding, f.Recommendation)));
        }

        return new ClientProgressDto
        {
            CompletedSessions = completedCount,
            WorkoutDays = logs.Select(l => l.WorkoutDate).Distinct().Count(),
            Since = firstSession is { } fs ? DateOnly.FromDateTime(fs)
                : logs.Count > 0 ? logs.Min(l => l.WorkoutDate) : null,
            Strength = strength,
            WeightFromKg = weights.Count >= 2 ? weights[0] : null,
            WeightToKg = weights.Count >= 2 ? weights[^1] : null,
            WaistFromCm = waists.Count >= 2 ? waists[0] : null,
            WaistToCm = waists.Count >= 2 ? waists[^1] : null,
            Attended30 = recent.Count(r => r.Status == SessionStatus.Completed),
            Missed30 = recent.Count(r => r.Status == SessionStatus.NoShow || (r.Status == SessionStatus.Cancelled && r.IsLateCancellation)),
            Insights = insights.Take(6).ToList(),
            TotalMinutes = totalMinutes,
            WeekStreak = ProgressSummaryWeeks.Streak(completedDates.Select(DateOnly.FromDateTime), DateOnly.FromDateTime(now))
        };
    }
}
