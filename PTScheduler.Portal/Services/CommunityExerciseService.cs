using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Wspólna baza ćwiczeń („Społeczność”): trener za zgodą udostępnia własne ćwiczenie, inni trenerzy je przeglądają
/// i kopiują do siebie. Dostęp tylko z pakietem z hostingiem wideo (Bunny) — tak jak kursy.
/// </summary>
public class CommunityExerciseService(IDbContextFactory<PortalDbContext> dbFactory, VideoHubService videos)
{
    public sealed record ShareInput(int SourceExerciseId, string NamePl, string? NameEn, string? Description, string PrimaryMuscles,
        string? SecondaryMuscles, string? Category, string? Level, string? Equipment, string? Force, string? Mechanic, string? Tracking,
        List<string>? ImageUrls, string? VideoType, string? VideoRef, bool ShowAuthor, bool Consent);

    public sealed record Item(int Id, string NamePl, string? NameEn, string? Description, string PrimaryMuscles, string SecondaryMuscles,
        string Category, string Level, string? Equipment, string? Force, string? Mechanic, string? Tracking, string ImageUrls,
        string VideoType, string? VideoRef, string? AuthorName, bool Mine, int Copies, DateTime UpdatedAt);

    /// <summary>Czy pakiet trenera obejmuje Społeczność (pakiet z wideo Bunny).</summary>
    public async Task<bool> AllowedAsync(Tenant tenant)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await videos.QuotaBytesAsync(db, tenant) > 0;
    }

    public async Task<(bool Ok, string? Error, int? Id)> ShareAsync(Tenant tenant, ShareInput input)
    {
        if (!input.Consent) return (false, "Udostępnienie wymaga zgody.", null);
        if (string.IsNullOrWhiteSpace(input.NamePl) || string.IsNullOrWhiteSpace(input.PrimaryMuscles)) return (false, "Brak nazwy albo partii mięśniowej.", null);
        if (!await AllowedAsync(tenant)) return (false, "Społeczność jest dostępna w pakiecie z hostingiem wideo.", null);

        await using var db = await dbFactory.CreateDbContextAsync();
        // Wideo Bunny tylko własne — nie da się „podpiąć” cudzego filmu pod swoje ćwiczenie.
        var videoType = input.VideoType is "YouTube" or "Bunny" or "Url" ? input.VideoType : "None";
        var videoRef = videoType == "None" ? null : Clip(input.VideoRef, 500);
        if (videoType == "Bunny")
        {
            var guid = videoRef?.Split('/').Last();
            if (guid is null || !await db.TenantVideos.AnyAsync(v => v.TenantId == tenant.Id && v.VideoGuid == guid)) { videoType = "None"; videoRef = null; }
        }
        var images = (input.ImageUrls ?? []).Where(u => Uri.TryCreate(u, UriKind.Absolute, out var x) && x.Scheme == Uri.UriSchemeHttps).Take(4);

        var row = await db.CommunityExercises.FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.SourceExerciseId == input.SourceExerciseId);
        if (row is null)
        {
            row = new CommunityExercise { TenantId = tenant.Id, SourceExerciseId = input.SourceExerciseId };
            db.CommunityExercises.Add(row);
        }
        row.NamePl = Clip(input.NamePl, 200)!;
        row.NameEn = Clip(input.NameEn, 200);
        row.Description = Clip(input.Description, 4000);
        row.PrimaryMuscles = Clip(input.PrimaryMuscles, 300)!;
        row.SecondaryMuscles = Clip(input.SecondaryMuscles, 300) ?? "";
        row.Category = Clip(input.Category, 40) ?? "Strength";
        row.Level = Clip(input.Level, 20) ?? "Beginner";
        row.Equipment = Clip(input.Equipment, 60);
        row.Force = Clip(input.Force, 20);
        row.Mechanic = Clip(input.Mechanic, 20);
        row.Tracking = Clip(input.Tracking, 30);
        row.ImageUrls = string.Join(",", images);
        row.VideoType = videoType!;
        row.VideoRef = videoRef;
        row.AuthorName = input.ShowAuthor ? Clip(tenant.CompanyName, 200) : null;
        row.ConsentAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, null, row.Id);
    }

    public async Task<bool> UnshareAsync(Tenant tenant, int sourceExerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.CommunityExercises.Where(c => c.TenantId == tenant.Id && c.SourceExerciseId == sourceExerciseId).ExecuteDeleteAsync() > 0;
    }

    /// <summary>Id ćwiczeń tej instancji, które są w Społeczności — do znaczka „udostępnione” w katalogu.</summary>
    public async Task<List<int>> SharedIdsAsync(Tenant tenant)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.CommunityExercises.Where(c => c.TenantId == tenant.Id).Select(c => c.SourceExerciseId).ToListAsync();
    }

    public async Task<List<Item>> SearchAsync(Tenant tenant, string? q, string? muscle, int take = 60)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.CommunityExercises.AsNoTracking().Where(c => !c.Hidden);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.NamePl, like) || (c.NameEn != null && EF.Functions.ILike(c.NameEn, like)));
        }
        if (!string.IsNullOrWhiteSpace(muscle)) query = query.Where(c => c.PrimaryMuscles.Contains(muscle));
        return await query.OrderByDescending(c => c.Copies).ThenByDescending(c => c.UpdatedAt).Take(Math.Clamp(take, 1, 120))
            .Select(c => new Item(c.Id, c.NamePl, c.NameEn, c.Description, c.PrimaryMuscles, c.SecondaryMuscles, c.Category, c.Level,
                c.Equipment, c.Force, c.Mechanic, c.Tracking, c.ImageUrls, c.VideoType, c.VideoRef, c.AuthorName, c.TenantId == tenant.Id,
                c.Copies, c.UpdatedAt))
            .ToListAsync();
    }

    /// <summary>Kopia do własnego katalogu: zwraca dane ćwiczenia i liczy kopie.</summary>
    public async Task<Item?> CopyAsync(Tenant tenant, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var c = await db.CommunityExercises.FirstOrDefaultAsync(x => x.Id == id && !x.Hidden);
        if (c is null) return null;
        if (c.TenantId != tenant.Id) { c.Copies++; await db.SaveChangesAsync(); }
        return new Item(c.Id, c.NamePl, c.NameEn, c.Description, c.PrimaryMuscles, c.SecondaryMuscles, c.Category, c.Level, c.Equipment,
            c.Force, c.Mechanic, c.Tracking, c.ImageUrls, c.VideoType, c.VideoRef, c.AuthorName, c.TenantId == tenant.Id, c.Copies, c.UpdatedAt);
    }

    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];
}
