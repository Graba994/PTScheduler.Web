using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ProgressionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock) : IProgressionService
{
    /// <summary>Starsze treningi nie mówią już nic o aktualnej formie.</summary>
    private const int LookbackDays = 42;

    public async Task<List<ProgressionSuggestionDto>> GetSuggestionsAsync(string trainerUserId, bool isAdmin, int clientId) =>
        await ComputeAsync(trainerUserId, isAdmin, clientId);

    public async Task<Dictionary<int, int>> GetCountsAsync(string trainerUserId, bool isAdmin) =>
        (await ComputeAsync(trainerUserId, isAdmin, clientId: null))
            .GroupBy(s => s.ClientId)
            .ToDictionary(g => g.Key, g => g.Count());

    public async Task<bool> ApplyAsync(string trainerUserId, bool isAdmin, int planExerciseId, decimal newTargetKg)
    {
        if (newTargetKg < 0 || newTargetKg > 1000) return false;
        await using var db = dbFactory.CreateDbContext();
        var pe = await FindOwnedAsync(db, trainerUserId, isAdmin, planExerciseId);
        if (pe is null) return false;
        pe.TargetWeightKg = Math.Round(newTargetKg, 2);
        pe.ProgressionReviewedFor = await LastLogDateAsync(db, planExerciseId) ?? clock.Today;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DismissAsync(string trainerUserId, bool isAdmin, int planExerciseId)
    {
        await using var db = dbFactory.CreateDbContext();
        var pe = await FindOwnedAsync(db, trainerUserId, isAdmin, planExerciseId);
        if (pe is null) return false;
        pe.ProgressionReviewedFor = await LastLogDateAsync(db, planExerciseId) ?? clock.Today;
        await db.SaveChangesAsync();
        return true;
    }

    private static Task<PlanExercise?> FindOwnedAsync(ApplicationDbContext db, string trainerUserId, bool isAdmin, int planExerciseId) =>
        db.PlanExercises
            .Where(pe => pe.Id == planExerciseId)
            .Where(pe => isAdmin || pe.PlanDay!.Plan!.TrainerUserId == trainerUserId)
            .FirstOrDefaultAsync();

    private static async Task<DateOnly?> LastLogDateAsync(ApplicationDbContext db, int planExerciseId) =>
        await db.WorkoutLogs.Where(l => l.PlanExerciseId == planExerciseId)
            .OrderByDescending(l => l.WorkoutDate)
            .Select(l => (DateOnly?)l.WorkoutDate)
            .FirstOrDefaultAsync();

    private async Task<List<ProgressionSuggestionDto>> ComputeAsync(string trainerUserId, bool isAdmin, int? clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var since = clock.Today.AddDays(-LookbackDays);

        var items = await db.PlanExercises.AsNoTracking()
            .Where(pe => !pe.PlanDay!.Plan!.IsTemplate && pe.PlanDay.Plan.ClientId != null)
            .Where(pe => isAdmin || pe.PlanDay!.Plan!.TrainerUserId == trainerUserId)
            .Where(pe => clientId == null || pe.PlanDay!.Plan!.ClientId == clientId)
            .Where(pe => db.WorkoutLogs.Any(l => l.PlanExerciseId == pe.Id && l.WorkoutDate >= since))
            .Select(pe => new
            {
                pe.Id,
                pe.Sets,
                pe.Reps,
                pe.TargetWeightKg,
                pe.ProgressionReviewedFor,
                PlanId = pe.PlanDay!.PlanId,
                PlanName = pe.PlanDay.Plan!.Name,
                ClientId = pe.PlanDay.Plan.ClientId!.Value,
                DayLabel = pe.PlanDay.Label,
                DayOrder = pe.PlanDay.Order,
                pe.Order,
                pe.Exercise!.NamePl,
                pe.Exercise.PrimaryMuscles,
                pe.Exercise.Mechanic,
                pe.Exercise.Equipment
            })
            .ToListAsync();
        if (items.Count == 0) return [];

        var peIds = items.Select(i => i.Id).ToList();
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.PlanExerciseId != null && peIds.Contains(l.PlanExerciseId.Value))
            .OrderByDescending(l => l.WorkoutDate)
            .Select(l => new
            {
                PlanExerciseId = l.PlanExerciseId!.Value,
                l.WorkoutDate,
                Sets = l.Sets.Select(s => new SetResult(s.Reps, s.WeightKg)).ToList()
            })
            .ToListAsync();

        var clientIds = items.Select(i => i.ClientId).Distinct().ToList();
        var surveys = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.Kind == SurveyKind.PostWorkout && clientIds.Contains(r.ClientId) && r.WorkoutDate >= since.AddDays(-LookbackDays))
            .Select(r => new { r.ClientId, r.WorkoutDate, r.SubmittedAt, r.AnswersJson })
            .ToListAsync();
        var rpeByDay = surveys
            .GroupBy(r => (r.ClientId, Date: r.WorkoutDate!.Value))
            .ToDictionary(g => g.Key, g =>
                SurveyJson.Deserialize(g.OrderByDescending(r => r.SubmittedAt).First().AnswersJson, new List<SurveyAnswer>())
                    .FirstOrDefault(a => a.Key == SurveyKeys.Rpe)?.Number);

        var result = new List<(ProgressionSuggestionDto Dto, (int, int, int) Sort)>();
        foreach (var it in items)
        {
            // Kilka logów tego samego dnia (np. zapis w dwóch częściach) scalamy w jedną sesję.
            var sessions = logs.Where(l => l.PlanExerciseId == it.Id)
                .GroupBy(l => l.WorkoutDate)
                .OrderByDescending(g => g.Key)
                .Take(2)
                .Select(g => (Date: g.Key, Sets: g.SelectMany(x => x.Sets).ToList()))
                .ToList();
            if (sessions.Count == 0) continue;
            var last = sessions[0];
            if (last.Date < since) continue;
            if (it.ProgressionReviewedFor is DateOnly seen && seen >= last.Date) continue;

            var rpe = rpeByDay.TryGetValue((it.ClientId, last.Date), out var v) ? v : null;
            var decision = ProgressionRules.Decide(it.Sets, it.Reps, last.Sets,
                sessions.Count > 1 ? sessions[1].Sets : null, rpe, it.PrimaryMuscles, it.Mechanic, it.Equipment);
            if (decision is null) continue;

            // Trener już przestawił ciężar w planie — nie podpowiadamy drugi raz.
            if (decision.Action == ProgressionAction.Increase && it.TargetWeightKg >= decision.ToKg) continue;
            if (decision.Action == ProgressionAction.Deload && it.TargetWeightKg is decimal t && t <= decision.ToKg) continue;

            var top = last.Sets.Max(s => s.WeightKg);
            result.Add((new ProgressionSuggestionDto
            {
                ClientId = it.ClientId,
                PlanExerciseId = it.Id,
                PlanId = it.PlanId,
                PlanName = it.PlanName,
                DayLabel = it.DayLabel,
                ExerciseName = it.NamePl,
                PlannedSets = it.Sets,
                Reps = it.Reps,
                CurrentTargetKg = it.TargetWeightKg,
                LastWorkoutDate = last.Date,
                LastRepsText = string.Join(" / ", last.Sets.Where(s => s.WeightKg == top).Select(s => s.Reps)),
                Rpe = rpe,
                Action = decision.Action,
                FromKg = decision.FromKg,
                SuggestedKg = decision.ToKg,
                Reason = decision.Reason
            }, (it.PlanId, it.DayOrder, it.Order)));
        }

        return result
            .OrderBy(r => r.Dto.Action) // najpierw podbicia, potem zejścia
            .ThenBy(r => r.Sort)
            .Select(r => r.Dto)
            .ToList();
    }
}
