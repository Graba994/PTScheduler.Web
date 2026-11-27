using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services.Studio;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Biblioteka treści w jednym miejscu: grafiki z postów, zrzuty aplikacji ze Studia (konto demo — bez danych prawdziwych osób),
/// gotowe filmy, Hantelek i własne pliki. Każdy materiał ma tytuł i tagi; „Użyj” dodaje go do posta (nowego albo otwartego).
/// Zrzuty z Garminem są oznaczone — w reklamie tylko zgodnie z zasadami marki Garmin.
/// </summary>
public class LibraryService(
    IDbContextFactory<PortalDbContext> dbFactory,
    InstagramService ig,
    StudioScreens screens,
    StudioVideos videos,
    FilmPublisher films,
    IWebHostEnvironment env)
{
    public sealed record Entry(string Source, string Ref, string Kind, string Title, List<string> Tags, string? Thumb, int Width, int Height,
        string? Note, DateTime CreatedAt, int Uses, int? LibraryId);

    public static readonly (string Key, string Label, string Icon)[] Sources =
    [
        ("upload", "Moje pliki", "bi-upload"), ("post", "Grafiki z postów", "bi-image"), ("screen", "Zrzuty aplikacji", "bi-phone"),
        ("film", "Filmy", "bi-film"), ("mascot", "Hantelek", "bi-emoji-smile")
    ];

    public static List<string> ParseTags(string? raw) =>
        (raw ?? "").Split([',', ';', '#'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant()).Where(t => t.Length is > 0 and <= 40).Distinct().Take(15).ToList();

    private string MascotDir => Path.Combine(env.WebRootPath ?? "wwwroot", "mascot");

    public async Task<List<Entry>> ListAsync(string? source = null, string? query = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var meta = await db.LibraryItems.AsNoTracking().Select(i => new { i.Id, i.Source, i.Ref, i.Title, i.Tags, i.Uses, i.Width, i.Height, i.CreatedAt })
            .ToListAsync();
        var byKey = meta.ToDictionary(m => (m.Source, m.Ref));
        var list = new List<Entry>();

        Entry Make(string src, string rf, string kind, string title, string? thumb, int w, int h, string? note, DateTime at, string defaultTags = "")
        {
            var m = byKey.GetValueOrDefault((src, rf));
            return new Entry(src, rf, kind, string.IsNullOrWhiteSpace(m?.Title) ? title : m!.Title, ParseTags(m?.Tags ?? defaultTags), thumb, w, h, note, at, m?.Uses ?? 0, m?.Id);
        }

        if (source is null or "upload")
            foreach (var m in meta.Where(m => m.Source == "upload"))
                list.Add(new Entry("upload", m.Ref, "image", m.Title, ParseTags(m.Tags), $"/api/panel/library/{m.Id}.jpg", m.Width, m.Height, null, m.CreatedAt, m.Uses, m.Id));

        if (source is null or "post")
        {
            var assets = await db.InstagramAssets.AsNoTracking().Where(a => a.Kind == "image" && a.Data != null)
                .OrderByDescending(a => a.CreatedAt).Take(200)
                .Join(db.InstagramPosts, a => a.PostId, p => p.Id, (a, p) => new { a.Token, a.Width, a.Height, a.CreatedAt, p.Title, p.Pillar })
                .ToListAsync();
            foreach (var a in assets)
                list.Add(Make("post", a.Token, "image", string.IsNullOrWhiteSpace(a.Title) ? "Grafika posta" : a.Title, $"/media/ig/{a.Token}.jpg",
                    a.Width, a.Height, null, a.CreatedAt, InstagramRules.FindPillar(a.Pillar)?.Label.ToLowerInvariant() ?? ""));
        }

        if (source is null or "screen")
        {
            var state = await screens.StateAsync();
            foreach (var s in state?.Screens.Where(s => s.Good) ?? [])
                list.Add(Make("screen", s.File, "image", string.IsNullOrWhiteSpace(s.Title) ? s.File : s.Title, $"/api/panel/studio/screens/{s.File}",
                    s.W, s.H, StudioScreens.IsGarmin(s) ? "Garmin — tylko zgodnie z zasadami marki (Garmin Connect™)" : null,
                    state!.UpdatedAt ?? DateTime.UtcNow, string.Join(',', new[] { s.Feature, s.As == "client" ? "widok klienta" : "widok trenera" }.Where(t => t.Length > 0))));
        }

        if (source is null or "film")
            foreach (var v in await db.StudioVideos.AsNoTracking()
                         .Where(v => v.Status == StudioVideoStatus.ReadyToPublish || v.Status == StudioVideoStatus.Published)
                         .OrderByDescending(v => v.Id).Take(50).ToListAsync())
                if (videos.HasVideo(v))
                    list.Add(Make("film", v.Id.ToString(), "video", v.Title, null, 1080, 1920, v.Garmin ? "Film z Garminem" : null, v.RenderedAt ?? v.CreatedAt, "film, 9:16"));

        if ((source is null or "mascot") && Directory.Exists(MascotDir))
            foreach (var f in Directory.GetFiles(MascotDir, "*.png").OrderBy(f => f))
            {
                var name = Path.GetFileName(f);
                list.Add(Make("mascot", name, "image", $"Hantelek — {Path.GetFileNameWithoutExtension(f)}", $"/mascot/{name}", 0, 0, null, File.GetLastWriteTimeUtc(f), "hantelek"));
            }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            list = list.Where(e => e.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Tags.Any(t => t.Contains(q))).ToList();
        }
        return list.OrderByDescending(e => e.Uses > 0).ThenByDescending(e => e.CreatedAt).ToList();
    }

    public async Task<(bool Ok, string Message)> UploadAsync(byte[] data, string title, string tags)
    {
        if (data.Length > 15 * 1024 * 1024) return (false, "Plik jest za duży (najwięcej 15 MB).");
        var (w, h) = InstagramRules.ImageSize(data);
        if (w == 0) return (false, "To nie jest zdjęcie (JPG, PNG albo WebP).");
        await using var db = await dbFactory.CreateDbContextAsync();
        db.LibraryItems.Add(new LibraryItem
        {
            Source = "upload", Ref = Guid.NewGuid().ToString("N"), Title = Clip(title.Trim(), 200), Tags = string.Join(',', ParseTags(tags)),
            Data = data, Width = w, Height = h
        });
        await db.SaveChangesAsync();
        return (true, "Dodano do biblioteki.");
    }

    public async Task<byte[]?> UploadBytesAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.LibraryItems.AsNoTracking().Where(i => i.Id == id && i.Source == "upload").Select(i => i.Data).FirstOrDefaultAsync();
    }

    public async Task SaveMetaAsync(string source, string reference, string title, string tags)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.LibraryItems.FirstOrDefaultAsync(i => i.Source == source && i.Ref == reference);
        if (item is null) db.LibraryItems.Add(item = new LibraryItem { Source = source, Ref = Clip(reference, 200) });
        item.Title = Clip(title.Trim(), 200);
        item.Tags = string.Join(',', ParseTags(tags));
        await db.SaveChangesAsync();
    }

    public async Task DeleteUploadAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.LibraryItems.Where(i => i.Id == id && i.Source == "upload").ExecuteDeleteAsync();
    }

    /// <summary>Bajty obrazu materiału (do dodania do posta) — null dla filmów i braków.</summary>
    private async Task<byte[]?> BytesAsync(string source, string reference)
    {
        switch (source)
        {
            case "upload":
                await using (var db = await dbFactory.CreateDbContextAsync())
                    return await db.LibraryItems.AsNoTracking().Where(i => i.Source == "upload" && i.Ref == reference).Select(i => i.Data).FirstOrDefaultAsync();
            case "post":
                return await ig.AssetBytesAsync(reference);
            case "screen":
                if (!StudioScreens.SafeName(reference)) return null;
                var path = Path.Combine(screens.ScreensDir, reference);
                return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
            case "mascot":
                if (!StudioScreens.SafeName(reference)) return null;
                var m = Path.Combine(MascotDir, reference);
                return File.Exists(m) ? await File.ReadAllBytesAsync(m) : null;
            default:
                return null;
        }
    }

    /// <summary>Dodaje materiał do posta (albo tworzy nowy szkic). Zwraca id posta.</summary>
    public async Task<(bool Ok, string Message, int? PostId)> UseAsync(string source, string reference, int? postId, string title)
    {
        if (source == "film")
        {
            if (!int.TryParse(reference, out var filmId)) return (false, "Nie ma takiego filmu.", null);
            if (postId is null) return await films.ToSocialAsync(filmId);
            var v = await videos.GetAsync(filmId);
            if (v is null) return (false, "Nie ma takiego filmu.", null);
            var r = await ig.AddVideoAsync(postId.Value, await films.PublicUrlAsync(v));
            await CountAsync(source, reference);
            return (r.Ok, r.Message, postId);
        }
        var data = await BytesAsync(source, reference);
        if (data is null) return (false, "Nie znaleziono pliku — mógł zostać usunięty.", null);
        var id = postId ?? await ig.CreatePostAsync(InstagramPostKind.Image, title, "", "manual");
        var (ok, message) = await ig.AddPhotoAsync(id, data);
        if (!ok) return (false, message, postId);
        await CountAsync(source, reference);
        var garmin = source == "screen" && (await screens.StateAsync())?.Screens.FirstOrDefault(s => s.File == reference) is { } s && StudioScreens.IsGarmin(s);
        return (true, garmin ? "Dodano. To zrzut z Garminem — w opisie dopisz informację o znakach towarowych Garmin i nie sugeruj współpracy z marką." : "Dodano do posta.", id);
    }

    private async Task CountAsync(string source, string reference)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.LibraryItems.FirstOrDefaultAsync(i => i.Source == source && i.Ref == reference);
        if (item is null) db.LibraryItems.Add(item = new LibraryItem { Source = source, Ref = Clip(reference, 200) });
        item.Uses++;
        await db.SaveChangesAsync();
    }

    /// <summary>Typ obrazu po nagłówku pliku (PNG, WebP, inaczej JPEG).</summary>
    public static string ContentType(byte[] d) =>
        d.Length > 4 && d[0] == 0x89 && d[1] == 'P' ? "image/png"
        : d.Length > 12 && d[0] == 'R' && d[8] == 'W' && d[9] == 'E' ? "image/webp"
        : "image/jpeg";

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}
