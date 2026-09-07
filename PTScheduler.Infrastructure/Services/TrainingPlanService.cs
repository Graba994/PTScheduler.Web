using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class TrainingPlanService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock) : ITrainingPlanService
{
    public async Task<List<TrainingPlanListItemDto>> GetPlansAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.TrainingPlans.AsNoTracking()
            .Where(p => p.TrainerUserId == trainerUserId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new TrainingPlanListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                IsTemplate = p.IsTemplate,
                ClientId = p.ClientId,
                ClientName = p.Client != null ? p.Client.FirstName + " " + p.Client.LastName : null,
                DayCount = p.Days.Count,
                ExerciseCount = p.Days.SelectMany(d => d.Exercises).Count(),
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PlanEditDto?> GetForEditAsync(string trainerUserId, int planId)
    {
        await using var db = dbFactory.CreateDbContext();
        var plan = await db.TrainingPlans.AsNoTracking()
            .Include(p => p.Days).ThenInclude(d => d.Exercises)
            .FirstOrDefaultAsync(p => p.Id == planId && p.TrainerUserId == trainerUserId);
        if (plan is null) return null;
        return await BuildEditDtoAsync(db, plan);
    }

    public async Task<List<TrainingPlanListItemDto>> GetClientPlansAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.TrainingPlans.AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new TrainingPlanListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                IsTemplate = p.IsTemplate,
                ClientId = p.ClientId,
                DayCount = p.Days.Count,
                ExerciseCount = p.Days.SelectMany(d => d.Exercises).Count(),
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<PlanEditDto?> GetForWorkoutAsync(int clientId, int planId)
    {
        await using var db = dbFactory.CreateDbContext();
        var plan = await db.TrainingPlans.AsNoTracking()
            .Include(p => p.Days).ThenInclude(d => d.Exercises)
            .FirstOrDefaultAsync(p => p.Id == planId && p.ClientId == clientId);
        if (plan is null) return null;
        return await BuildEditDtoAsync(db, plan);
    }

    private static async Task<PlanEditDto> BuildEditDtoAsync(ApplicationDbContext db, TrainingPlan plan)
    {
        var exIds = plan.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).Distinct().ToList();
        var exInfo = await db.Exercises.AsNoTracking()
            .Where(e => exIds.Contains(e.Id))
            .Select(e => new { e.Id, e.NamePl, e.ImageUrls })
            .ToDictionaryAsync(e => e.Id);

        return new PlanEditDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Notes = plan.Notes,
            IsTemplate = plan.IsTemplate,
            ClientId = plan.ClientId,
            Days = plan.Days.OrderBy(d => d.Order).Select(d => new PlanDayEditDto
            {
                Id = d.Id,
                Order = d.Order,
                Label = d.Label,
                Exercises = d.Exercises.OrderBy(x => x.Order).Select(x =>
                {
                    exInfo.TryGetValue(x.ExerciseId, out var info);
                    return new PlanExerciseEditDto
                    {
                        Id = x.Id,
                        ExerciseId = x.ExerciseId,
                        ExerciseNamePl = info?.NamePl ?? $"#{x.ExerciseId}",
                        ThumbnailUrl = info is null ? null : FirstImage(info.ImageUrls),
                        Order = x.Order,
                        Sets = x.Sets,
                        Reps = x.Reps,
                        TargetWeightKg = x.TargetWeightKg,
                        Tempo = x.Tempo,
                        RestSeconds = x.RestSeconds,
                        Notes = x.Notes
                    };
                }).ToList()
            }).ToList()
        };
    }

    public async Task<int> SavePlanAsync(string trainerUserId, PlanEditDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("Podaj nazwę planu.");

        await using var db = dbFactory.CreateDbContext();

        TrainingPlan plan;
        if (dto.Id is > 0)
        {
            plan = await db.TrainingPlans
                .Include(p => p.Days).ThenInclude(d => d.Exercises)
                .FirstOrDefaultAsync(p => p.Id == dto.Id && p.TrainerUserId == trainerUserId)
                ?? throw new InvalidOperationException("Plan nie istnieje.");
        }
        else
        {
            plan = new TrainingPlan { TrainerUserId = trainerUserId, CreatedAt = clock.UtcNow };
            db.TrainingPlans.Add(plan);
        }

        plan.Name = dto.Name.Trim();
        plan.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        plan.IsTemplate = dto.IsTemplate;
        plan.ClientId = dto.IsTemplate ? null : dto.ClientId;

        // Tylko ćwiczenia widoczne dla trenera (baza publiczna lub własne).
        var wantedExIds = dto.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).Distinct().ToList();
        var validExIds = (await db.Exercises
                .Where(e => wantedExIds.Contains(e.Id) &&
                    (e.Visibility == Domain.Enums.ExerciseVisibility.Public || e.OwnerTrainerUserId == trainerUserId))
                .Select(e => e.Id)
                .ToListAsync())
            .ToHashSet();

        // --- Dni: dopasuj po Id, usuń brakujące ---
        var keepDayIds = dto.Days.Where(d => d.Id is > 0).Select(d => d.Id!.Value).ToHashSet();
        foreach (var existingDay in plan.Days.ToList())
            if (!keepDayIds.Contains(existingDay.Id))
                db.PlanDays.Remove(existingDay);

        for (var di = 0; di < dto.Days.Count; di++)
        {
            var dd = dto.Days[di];
            var day = dd.Id is > 0 ? plan.Days.FirstOrDefault(d => d.Id == dd.Id) : null;
            if (day is null)
            {
                day = new PlanDay();
                plan.Days.Add(day);
            }
            day.Order = di;
            day.Label = dd.Label?.Trim() ?? string.Empty;

            var keepExIds = dd.Exercises.Where(x => x.Id is > 0).Select(x => x.Id!.Value).ToHashSet();
            foreach (var existingEx in day.Exercises.ToList())
                if (!keepExIds.Contains(existingEx.Id))
                    db.PlanExercises.Remove(existingEx);

            var order = 0;
            foreach (var xe in dd.Exercises)
            {
                if (!validExIds.Contains(xe.ExerciseId)) continue; // pomiń niewidoczne/nieistniejące
                var pe = xe.Id is > 0 ? day.Exercises.FirstOrDefault(p => p.Id == xe.Id) : null;
                if (pe is null)
                {
                    pe = new PlanExercise();
                    day.Exercises.Add(pe);
                }
                pe.ExerciseId = xe.ExerciseId;
                pe.Order = order++;
                pe.Sets = xe.Sets;
                pe.Reps = string.IsNullOrWhiteSpace(xe.Reps) ? null : xe.Reps.Trim();
                pe.TargetWeightKg = xe.TargetWeightKg;
                pe.Tempo = string.IsNullOrWhiteSpace(xe.Tempo) ? null : xe.Tempo.Trim();
                pe.RestSeconds = xe.RestSeconds;
                pe.Notes = string.IsNullOrWhiteSpace(xe.Notes) ? null : xe.Notes.Trim();
            }
        }

        // „Ostatnio używane" dla ćwiczeń faktycznie wstawionych do planu.
        await TouchLastUsedAsync(db, trainerUserId, validExIds);

        await db.SaveChangesAsync();
        return plan.Id;
    }

    public async Task DeletePlanAsync(string trainerUserId, int planId)
    {
        await using var db = dbFactory.CreateDbContext();
        var plan = await db.TrainingPlans.FirstOrDefaultAsync(p => p.Id == planId && p.TrainerUserId == trainerUserId);
        if (plan is null) return;
        db.TrainingPlans.Remove(plan); // kaskada usuwa dni i ćwiczenia planu
        await db.SaveChangesAsync();
    }

    public async Task<int> DuplicateAsync(string trainerUserId, int planId, bool asTemplate)
    {
        await using var db = dbFactory.CreateDbContext();
        var src = await db.TrainingPlans.AsNoTracking()
            .Include(p => p.Days).ThenInclude(d => d.Exercises)
            .FirstOrDefaultAsync(p => p.Id == planId && p.TrainerUserId == trainerUserId)
            ?? throw new InvalidOperationException("Plan nie istnieje.");

        var copy = new TrainingPlan
        {
            TrainerUserId = trainerUserId,
            Name = src.Name + " (kopia)",
            Notes = src.Notes,
            IsTemplate = asTemplate,
            ClientId = null,
            CreatedAt = clock.UtcNow,
            Days = src.Days.OrderBy(d => d.Order).Select(d => new PlanDay
            {
                Order = d.Order,
                Label = d.Label,
                Exercises = d.Exercises.OrderBy(x => x.Order).Select(x => new PlanExercise
                {
                    ExerciseId = x.ExerciseId,
                    Order = x.Order,
                    Sets = x.Sets,
                    Reps = x.Reps,
                    TargetWeightKg = x.TargetWeightKg,
                    Tempo = x.Tempo,
                    RestSeconds = x.RestSeconds,
                    Notes = x.Notes
                }).ToList()
            }).ToList()
        };
        db.TrainingPlans.Add(copy);
        await db.SaveChangesAsync();
        return copy.Id;
    }

    private async Task TouchLastUsedAsync(ApplicationDbContext db, string trainerUserId, IEnumerable<int> exerciseIds)
    {
        var ids = exerciseIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var prefs = await db.TrainerExercisePrefs
            .Where(p => p.TrainerUserId == trainerUserId && ids.Contains(p.ExerciseId))
            .ToListAsync();
        var have = prefs.ToDictionary(p => p.ExerciseId);
        foreach (var id in ids)
        {
            if (have.TryGetValue(id, out var pref)) pref.LastUsedAt = clock.UtcNow;
            else db.TrainerExercisePrefs.Add(new TrainerExercisePref
            {
                TrainerUserId = trainerUserId, ExerciseId = id, LastUsedAt = clock.UtcNow
            });
        }
    }

    private static string? FirstImage(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return null;
        var i = csv.IndexOf(',');
        return i < 0 ? csv : csv[..i];
    }
}
