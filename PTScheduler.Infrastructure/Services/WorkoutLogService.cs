using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class WorkoutLogService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock) : IWorkoutLogService
{
    public async Task<int> LogWorkoutAsync(int clientId, LogWorkoutDto dto)
    {
        await using var db = dbFactory.CreateDbContext();

        var date = dto.Date == default ? clock.Today : dto.Date;

        // Tylko istniejące ćwiczenia (unikamy błędu FK na nieznanym Id).
        var wantedExIds = dto.Exercises.Select(e => e.ExerciseId).Distinct().ToList();
        var validExIds = (await db.Exercises
                .Where(e => wantedExIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync())
            .ToHashSet();

        // Poprawność Id pozycji planu (jeśli podane) — inaczej zapisujemy jako luźne.
        var wantedPeIds = dto.Exercises.Where(e => e.PlanExerciseId is > 0)
            .Select(e => e.PlanExerciseId!.Value).Distinct().ToList();
        var validPeIds = wantedPeIds.Count == 0
            ? new HashSet<int>()
            : (await db.PlanExercises.Where(pe => wantedPeIds.Contains(pe.Id)).Select(pe => pe.Id).ToListAsync()).ToHashSet();

        var savedSets = 0;
        foreach (var ex in dto.Exercises)
        {
            if (!validExIds.Contains(ex.ExerciseId)) continue;
            var sets = ex.Sets
                .Where(s => s.Reps > 0 || s.WeightKg > 0) // pomiń puste serie
                .OrderBy(s => s.SetNumber)
                .ToList();
            if (sets.Count == 0) continue;

            var log = new WorkoutLog
            {
                ClientId = clientId,
                ExerciseId = ex.ExerciseId,
                PlanExerciseId = ex.PlanExerciseId is > 0 && validPeIds.Contains(ex.PlanExerciseId.Value)
                    ? ex.PlanExerciseId : null,
                WorkoutDate = date,
                CreatedAt = clock.UtcNow,
                Sets = sets.Select((s, i) => new WorkoutSetLog
                {
                    SetNumber = i + 1,
                    Reps = s.Reps,
                    WeightKg = s.WeightKg
                }).ToList()
            };
            db.WorkoutLogs.Add(log);
            savedSets += log.Sets.Count;
        }

        if (savedSets > 0)
            await db.SaveChangesAsync();
        return savedSets;
    }

    public async Task<List<WorkoutHistoryItemDto>> GetRecentForClientAsync(int clientId, int take = 20)
    {
        await using var db = dbFactory.CreateDbContext();
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.ClientId == clientId)
            .Include(w => w.Sets)
            .ToListAsync();

        return logs
            .GroupBy(w => w.WorkoutDate)
            .Select(g => new WorkoutHistoryItemDto
            {
                Date = g.Key,
                ExerciseCount = g.Select(l => l.ExerciseId).Distinct().Count(),
                SetCount = g.Sum(l => l.Sets.Count),
                TotalVolume = g.Sum(l => VolumeCalculator.TotalVolume(l.Sets))
            })
            .OrderByDescending(h => h.Date)
            .Take(take)
            .ToList();
    }
}
