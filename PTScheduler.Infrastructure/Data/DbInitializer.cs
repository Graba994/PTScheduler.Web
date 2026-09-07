using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data.SeedData;

namespace PTScheduler.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedPermissionsAsync(ApplicationDbContext db)
    {
        // Idempotent: add any missing (role, permission) rows without touching
        // existing ones, so newly introduced permissions appear for management
        // on databases that were seeded before those permissions existed.
        var existing = (await db.RolePermissions
                .Select(rp => new { rp.Role, rp.Permission })
                .ToListAsync())
            .Select(e => (e.Role, e.Permission))
            .ToHashSet();

        var toAdd = new List<RolePermission>();
        foreach (var (role, permissions) in Permissions.Defaults)
        {
            foreach (var perm in Permissions.All)
            {
                if (existing.Contains((role, perm.Key))) continue;
                toAdd.Add(new RolePermission
                {
                    Role = role,
                    Permission = perm.Key,
                    IsGranted = permissions.Contains(perm.Key)
                });
            }
        }

        if (toAdd.Count > 0)
        {
            db.RolePermissions.AddRange(toAdd);
            await db.SaveChangesAsync();
        }
    }

    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles = [Roles.Admin, Roles.Trainer, Roles.Subordinate, Roles.Client];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    public static async Task SeedAdminAsync(UserManager<ApplicationUser> userManager)
    {
        const string email = "root@admin.local";
        if (await userManager.FindByEmailAsync(email) is not null) return;

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "Admin",
            LastName = "System",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        await userManager.CreateAsync(admin);
        admin.PasswordHash = userManager.PasswordHasher.HashPassword(admin, "password");
        await userManager.UpdateAsync(admin);
        await userManager.AddToRoleAsync(admin, Roles.Admin);
    }

    public static async Task SeedSessionTypesAsync(ApplicationDbContext db)
    {
        if (await db.SessionTypes.AnyAsync()) return;

        db.SessionTypes.AddRange(
            new SessionType { Name = "45 minut", DurationMinutes = 45, IsActive = true },
            new SessionType { Name = "60 minut (standard)", DurationMinutes = 60, IsActive = true },
            new SessionType { Name = "90 minut", DurationMinutes = 90, IsActive = true },
            new SessionType { Name = "Trening grupowy", DurationMinutes = 60, IsGroup = true, IsActive = true }
        );
        await db.SaveChangesAsync();
    }

    private static readonly JsonSerializerOptions SeedJsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Zasila katalog ćwiczeniami bazowymi z Free Exercise DB (zasób osadzony).
    /// Idempotentnie: dokłada tylko rekordy, których jeszcze nie ma (po
    /// SourceKey = id FED), więc ponowne uruchomienie dodaje nowe pozycje i nie
    /// nadpisuje ćwiczeń, które trener mógł już edytować. Nazwy PL bierze z
    /// warstwy nadpisań (exercise-names-pl.json); dla reszty zostaje nazwa EN
    /// (opis EN dostępny zawsze). Bazę obrazów można nadpisać zmienną
    /// EXERCISE_IMAGE_BASE_URL (domyślnie repo Free Exercise DB).
    /// </summary>
    public static async Task SeedExerciseCatalogAsync(ApplicationDbContext db)
    {
        var asm = typeof(DbInitializer).Assembly;
        var records = ReadEmbeddedJson<List<FreeExerciseDbRecord>>(asm, "free-exercise-db.json");
        if (records is null || records.Count == 0) return;

        var plNames = ReadEmbeddedJson<Dictionary<string, string>>(asm, "exercise-names-pl.json")
                      ?? new Dictionary<string, string>();

        var have = (await db.Exercises
                .Where(e => e.SourceKey != null)
                .Select(e => e.SourceKey!)
                .ToListAsync())
            .ToHashSet();

        var imageBase = (Environment.GetEnvironmentVariable("EXERCISE_IMAGE_BASE_URL")
            ?? "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/")
            .TrimEnd('/') + "/";

        var toAdd = new List<Exercise>();
        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.Id) || have.Contains(r.Id)) continue;

            var namePl = plNames.TryGetValue(r.Id, out var pl) && !string.IsNullOrWhiteSpace(pl)
                ? pl : r.Name;

            toAdd.Add(new Exercise
            {
                OwnerTrainerUserId = null,
                Visibility = ExerciseVisibility.Public,
                NameEn = r.Name,
                NamePl = namePl,
                DescriptionEn = r.Instructions.Length > 0 ? string.Join("\n", r.Instructions) : null,
                DescriptionPl = null,
                PrimaryMuscles = string.Join(",", r.PrimaryMuscles),
                SecondaryMuscles = string.Join(",", r.SecondaryMuscles),
                Category = MapCategory(r.Category),
                Level = MapLevel(r.Level),
                Equipment = r.Equipment,
                Force = r.Force,
                Mechanic = r.Mechanic,
                ImageUrls = string.Join(",", r.Images.Select(p => imageBase + p.TrimStart('/'))),
                VideoType = ExerciseVideoType.None,
                VideoRef = null,
                SourceKey = r.Id
            });
        }

        if (toAdd.Count > 0)
        {
            db.Exercises.AddRange(toAdd);
            await db.SaveChangesAsync();
        }
    }

    private static T? ReadEmbeddedJson<T>(Assembly asm, string endsWith)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
        if (name is null) return default;
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return default;
        return JsonSerializer.Deserialize<T>(stream, SeedJsonOpts);
    }

    private static ExerciseCategory MapCategory(string? c) => c switch
    {
        "strength" => ExerciseCategory.Strength,
        "stretching" => ExerciseCategory.Stretching,
        "plyometrics" => ExerciseCategory.Plyometrics,
        "strongman" => ExerciseCategory.Strongman,
        "powerlifting" => ExerciseCategory.Powerlifting,
        "cardio" => ExerciseCategory.Cardio,
        "olympic weightlifting" => ExerciseCategory.OlympicWeightlifting,
        _ => ExerciseCategory.Other
    };

    private static ExerciseLevel MapLevel(string? l) => l switch
    {
        "beginner" => ExerciseLevel.Beginner,
        "intermediate" => ExerciseLevel.Intermediate,
        "expert" => ExerciseLevel.Expert,
        _ => ExerciseLevel.Beginner
    };
}
