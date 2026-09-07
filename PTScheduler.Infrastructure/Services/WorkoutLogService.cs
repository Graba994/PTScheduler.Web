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

    public async Task<List<VolumePointDto>> GetVolumeOverTimeAsync(int clientId, int days = 90)
    {
        await using var db = dbFactory.CreateDbContext();
        var from = clock.Today.AddDays(-days);
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.ClientId == clientId && w.WorkoutDate >= from)
            .Include(w => w.Sets)
            .ToListAsync();

        return logs.GroupBy(w => w.WorkoutDate)
            .Select(g => new VolumePointDto { Date = g.Key, Volume = g.Sum(l => VolumeCalculator.TotalVolume(l.Sets)) })
            .OrderBy(p => p.Date)
            .ToList();
    }

    public async Task<List<MuscleVolumeDto>> GetVolumeByMuscleAsync(int clientId, int days = 90)
    {
        await using var db = dbFactory.CreateDbContext();
        var from = clock.Today.AddDays(-days);
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.ClientId == clientId && w.WorkoutDate >= from)
            .Include(w => w.Sets)
            .Include(w => w.Exercise)
            .ToListAsync();

        var pairs = logs.Select(w => (w.Exercise!.PrimaryMuscles, VolumeCalculator.TotalVolume(w.Sets)));
        return VolumeCalculator.VolumeByMuscle(pairs)
            .Select(kv => new MuscleVolumeDto { Muscle = kv.Key, Label = Muscles.Label(kv.Key), Volume = kv.Value })
            .OrderByDescending(m => m.Volume)
            .ToList();
    }

    public async Task<List<PersonalRecordDto>> GetPersonalRecordsAsync(int clientId, int take = 12)
    {
        await using var db = dbFactory.CreateDbContext();
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.ClientId == clientId)
            .Include(w => w.Sets)
            .Include(w => w.Exercise)
            .ToListAsync();

        return logs.GroupBy(w => w.ExerciseId)
            .Select(g =>
            {
                var sets = g.SelectMany(l => l.Sets).ToList();
                var maxWeight = sets.Count > 0 ? sets.Max(s => s.WeightKg) : 0m;
                return new PersonalRecordDto
                {
                    ExerciseId = g.Key,
                    ExerciseName = g.First().Exercise!.NamePl,
                    MaxWeight = maxWeight,
                    RepsAtMax = sets.Where(s => s.WeightKg == maxWeight).Select(s => s.Reps).DefaultIfEmpty(0).Max(),
                    BestSetVolume = sets.Count > 0 ? sets.Max(s => s.Reps * s.WeightKg) : 0m
                };
            })
            .Where(r => r.MaxWeight > 0 || r.BestSetVolume > 0)
            .OrderByDescending(r => r.MaxWeight)
            .ThenByDescending(r => r.BestSetVolume)
            .Take(take)
            .ToList();
    }

    public async Task<List<WorkoutJournalDayDto>> GetJournalAsync(int clientId, int takeDays = 20)
    {
        await using var db = dbFactory.CreateDbContext();
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(w => w.ClientId == clientId)
            .Include(w => w.Sets)
            .Include(w => w.Exercise)
            .ToListAsync();

        return logs.GroupBy(w => w.WorkoutDate)
            .OrderByDescending(g => g.Key)
            .Take(takeDays)
            .Select(g => new WorkoutJournalDayDto
            {
                Date = g.Key,
                SetCount = g.Sum(l => l.Sets.Count),
                TotalVolume = g.Sum(l => VolumeCalculator.TotalVolume(l.Sets)),
                Exercises = g.Select(l => new WorkoutJournalExerciseDto
                {
                    ExerciseName = l.Exercise!.NamePl,
                    Volume = VolumeCalculator.TotalVolume(l.Sets),
                    Sets = l.Sets.OrderBy(s => s.SetNumber).Select(s => new WorkoutJournalSetDto
                    {
                        SetNumber = s.SetNumber,
                        Reps = s.Reps,
                        WeightKg = s.WeightKg
                    }).ToList()
                }).ToList()
            })
            .ToList();
    }

    public async Task<List<ClientActivityDto>> GetClientsActivityAsync(string trainerUserId, int days = 30)
    {
        await using var db = dbFactory.CreateDbContext();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.TrainerUserId == trainerUserId)
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .ToListAsync();
        if (clients.Count == 0) return [];

        var ids = clients.Select(c => c.Id).ToList();
        var from = clock.Today.AddDays(-days);
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(w => ids.Contains(w.ClientId))
            .Include(w => w.Sets)
            .ToListAsync();
        var byClient = logs.GroupBy(w => w.ClientId).ToDictionary(g => g.Key, g => g.ToList());

        return clients.Select(c =>
        {
            byClient.TryGetValue(c.Id, out var cl);
            cl ??= [];
            return new ClientActivityDto
            {
                ClientId = c.Id,
                ClientName = $"{c.FirstName} {c.LastName}".Trim(),
                LastWorkout = cl.Count > 0 ? cl.Max(w => w.WorkoutDate) : null,
                WorkoutsInWindow = cl.Where(w => w.WorkoutDate >= from).Select(w => w.WorkoutDate).Distinct().Count(),
                VolumeInWindow = cl.Where(w => w.WorkoutDate >= from).Sum(w => VolumeCalculator.TotalVolume(w.Sets))
            };
        })
        .OrderByDescending(a => a.LastWorkout ?? DateOnly.MinValue)
        .ThenBy(a => a.ClientName)
        .ToList();
    }
}
