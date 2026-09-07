using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ExerciseCatalogService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock) : IExerciseCatalogService
{
    public async Task<List<ExerciseListItemDto>> SearchAsync(string trainerUserId, ExerciseFilterDto filter, int take = 300)
    {
        await using var db = dbFactory.CreateDbContext();

        // Preferencje trenera (małe) — flagi ulubione/ostatnio-używane i sortowanie.
        var prefs = await db.TrainerExercisePrefs
            .Where(p => p.TrainerUserId == trainerUserId)
            .Select(p => new { p.ExerciseId, p.IsFavorite, p.LastUsedAt })
            .ToListAsync();
        var favIdList = prefs.Where(p => p.IsFavorite).Select(p => p.ExerciseId).ToList();
        var favIdSet = favIdList.ToHashSet();
        var lastUsed = prefs.Where(p => p.LastUsedAt != null)
            .ToDictionary(p => p.ExerciseId, p => p.LastUsedAt!.Value);
        var recentIdList = lastUsed.Keys.ToList();

        // Widoczne: baza publiczna + własne trenera.
        var q = db.Exercises.AsNoTracking()
            .Where(e => e.Visibility == ExerciseVisibility.Public || e.OwnerTrainerUserId == trainerUserId);

        q = filter.Scope switch
        {
            ExerciseCatalogScope.Public => q.Where(e => e.Visibility == ExerciseVisibility.Public),
            ExerciseCatalogScope.Mine => q.Where(e => e.OwnerTrainerUserId == trainerUserId),
            ExerciseCatalogScope.Favorites => q.Where(e => favIdList.Contains(e.Id)),
            ExerciseCatalogScope.Recent => q.Where(e => recentIdList.Contains(e.Id)),
            _ => q
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            q = q.Where(e => e.NamePl.ToLower().Contains(s) || e.NameEn.ToLower().Contains(s));
        }

        if (filter.Muscle is { } m)
        {
            var key = Muscles.ToCsv(new[] { m }); // kanoniczny klucz FED, np. "lower back"
            q = q.Where(e => e.PrimaryMuscles.Contains(key) || e.SecondaryMuscles.Contains(key));
        }

        if (filter.Category is { } c) q = q.Where(e => e.Category == c);
        if (filter.Level is { } l) q = q.Where(e => e.Level == l);
        if (!string.IsNullOrWhiteSpace(filter.Equipment)) q = q.Where(e => e.Equipment == filter.Equipment);

        var rows = await q
            .Select(e => new
            {
                e.Id, e.NamePl, e.NameEn, e.PrimaryMuscles, e.Category, e.Level,
                e.Equipment, e.ImageUrls, e.OwnerTrainerUserId, e.VideoType
            })
            .ToListAsync();

        var items = rows.Select(e => new ExerciseListItemDto
        {
            Id = e.Id,
            NamePl = e.NamePl,
            NameEn = e.NameEn,
            PrimaryMuscles = e.PrimaryMuscles,
            Category = e.Category,
            Level = e.Level,
            Equipment = e.Equipment,
            ThumbnailUrl = FirstImage(e.ImageUrls),
            IsMine = e.OwnerTrainerUserId == trainerUserId,
            IsFavorite = favIdSet.Contains(e.Id),
            HasVideo = e.VideoType != ExerciseVideoType.None
        });

        items = filter.Scope == ExerciseCatalogScope.Recent
            ? items.OrderByDescending(i => lastUsed.TryGetValue(i.Id, out var t) ? t : DateTime.MinValue)
            : items.OrderByDescending(i => i.IsFavorite).ThenBy(i => i.NamePl);

        return items.Take(take).ToList();
    }

    public async Task<ExerciseDetailDto?> GetAsync(string trainerUserId, int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var e = await db.Exercises.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id &&
                (x.Visibility == ExerciseVisibility.Public || x.OwnerTrainerUserId == trainerUserId));
        if (e is null) return null;

        var isFav = await db.TrainerExercisePrefs
            .AnyAsync(p => p.TrainerUserId == trainerUserId && p.ExerciseId == id && p.IsFavorite);

        return new ExerciseDetailDto
        {
            Id = e.Id,
            NamePl = e.NamePl,
            NameEn = e.NameEn,
            DescriptionPl = e.DescriptionPl,
            DescriptionEn = e.DescriptionEn,
            PrimaryMuscles = e.PrimaryMuscles,
            SecondaryMuscles = e.SecondaryMuscles,
            Category = e.Category,
            Level = e.Level,
            Equipment = e.Equipment,
            Force = e.Force,
            Mechanic = e.Mechanic,
            ImageUrls = SplitImages(e.ImageUrls),
            VideoType = e.VideoType,
            VideoRef = e.VideoRef,
            IsMine = e.OwnerTrainerUserId == trainerUserId,
            IsFavorite = isFav
        };
    }

    public async Task<int> CreateAsync(string trainerUserId, SaveExerciseDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var e = new Exercise
        {
            OwnerTrainerUserId = trainerUserId,
            Visibility = ExerciseVisibility.Mine,
            SourceKey = null
        };
        Apply(e, dto);
        db.Exercises.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    public async Task UpdateAsync(string trainerUserId, SaveExerciseDto dto)
    {
        if (dto.Id is not { } id) throw new InvalidOperationException("Brak Id ćwiczenia do edycji.");
        await using var db = dbFactory.CreateDbContext();
        var e = await db.Exercises.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) throw new InvalidOperationException("Ćwiczenie nie istnieje.");
        if (e.OwnerTrainerUserId != trainerUserId)
            throw new InvalidOperationException("Można edytować tylko własne ćwiczenia.");
        Apply(e, dto);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string trainerUserId, int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var e = await db.Exercises.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return;
        if (e.OwnerTrainerUserId != trainerUserId)
            throw new InvalidOperationException("Można usuwać tylko własne ćwiczenia.");

        var inUse = await db.PlanExercises.AnyAsync(pe => pe.ExerciseId == id)
                    || await db.WorkoutLogs.AnyAsync(w => w.ExerciseId == id);
        if (inUse)
            throw new InvalidOperationException("Ćwiczenie jest używane w planie lub dzienniku — nie można go usunąć.");

        // Usuń preferencje wskazujące na to ćwiczenie (FK i tak kaskaduje, ale
        // czyścimy jawnie na providerach bez kaskady).
        var prefs = db.TrainerExercisePrefs.Where(p => p.ExerciseId == id);
        db.TrainerExercisePrefs.RemoveRange(prefs);
        db.Exercises.Remove(e);
        await db.SaveChangesAsync();
    }

    public async Task<bool> ToggleFavoriteAsync(string trainerUserId, int exerciseId)
    {
        await using var db = dbFactory.CreateDbContext();
        var pref = await db.TrainerExercisePrefs
            .FirstOrDefaultAsync(p => p.TrainerUserId == trainerUserId && p.ExerciseId == exerciseId);
        if (pref is null)
        {
            pref = new TrainerExercisePref { TrainerUserId = trainerUserId, ExerciseId = exerciseId, IsFavorite = true };
            db.TrainerExercisePrefs.Add(pref);
            await db.SaveChangesAsync();
            return true;
        }
        pref.IsFavorite = !pref.IsFavorite;
        await db.SaveChangesAsync();
        return pref.IsFavorite;
    }

    public async Task MarkUsedAsync(string trainerUserId, int exerciseId)
    {
        await using var db = dbFactory.CreateDbContext();
        var pref = await db.TrainerExercisePrefs
            .FirstOrDefaultAsync(p => p.TrainerUserId == trainerUserId && p.ExerciseId == exerciseId);
        if (pref is null)
        {
            pref = new TrainerExercisePref { TrainerUserId = trainerUserId, ExerciseId = exerciseId };
            db.TrainerExercisePrefs.Add(pref);
        }
        pref.LastUsedAt = clock.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<string>> GetEquipmentOptionsAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Exercises.AsNoTracking()
            .Where(e => (e.Visibility == ExerciseVisibility.Public || e.OwnerTrainerUserId == trainerUserId)
                        && e.Equipment != null && e.Equipment != "")
            .Select(e => e.Equipment!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
    }

    private static void Apply(Exercise e, SaveExerciseDto dto)
    {
        e.NamePl = dto.NamePl.Trim();
        e.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NamePl.Trim() : dto.NameEn.Trim();
        e.DescriptionPl = dto.DescriptionPl;
        e.DescriptionEn = dto.DescriptionEn;
        e.PrimaryMuscles = Muscles.ToCsv(dto.PrimaryMuscles);
        e.SecondaryMuscles = Muscles.ToCsv(dto.SecondaryMuscles);
        e.Category = dto.Category;
        e.Level = dto.Level;
        e.Equipment = string.IsNullOrWhiteSpace(dto.Equipment) ? null : dto.Equipment.Trim();
        e.ImageUrls = string.Join(",", dto.ImageUrls
            .Select(u => u.Trim()).Where(u => u.Length > 0));
        e.VideoType = dto.VideoType;
        e.VideoRef = string.IsNullOrWhiteSpace(dto.VideoRef) ? null : dto.VideoRef.Trim();
    }

    private static string? FirstImage(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return null;
        var i = csv.IndexOf(',');
        return i < 0 ? csv : csv[..i];
    }

    private static IReadOnlyList<string> SplitImages(string csv) =>
        string.IsNullOrEmpty(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
