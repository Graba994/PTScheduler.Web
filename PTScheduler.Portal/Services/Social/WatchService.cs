using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Obserwacja konkurencji i hasztagów na Instagramie: publiczne dane kont firmowych (Business Discovery) i posty
/// z hasztagów, odświeżane raz dziennie. Wnioski AI z liczb i fragmentów opisów (bez e-maili i telefonów) — tylko podpowiedzi.
/// </summary>
public partial class WatchService(
    IDbContextFactory<PortalDbContext> dbFactory,
    WatchClient client,
    InstagramAi ai,
    IDataProtectionProvider dataProtection,
    ILogger<WatchService> logger)
{
    public const int MaxAccounts = 20, MaxHashtags = 10;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Summary(double? Engagement, double PostsPerWeek, int? FollowersDelta7, List<WatchClient.Media> Top);

    private IDataProtector Secrets => dataProtection.CreateProtector("instagram");

    /// <summary>Konto Instagram połączone przez stronę (tylko tak działa Business Discovery i hasztagi).</summary>
    public async Task<(string? IgId, string? Token, string? Error)> ConnectionAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var a = await db.InstagramAccounts.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (a is null) return (null, null, "Połącz Instagram w zakładce Kanały.");
        if (a.Provider != "facebook") return (null, null, "Obserwacja działa dla Instagrama połączonego przez stronę na Facebooku (Kanały → „Facebook + Instagram”).");
        try { return (a.IgUserId, Secrets.Unprotect(a.Token), null); }
        catch (CryptographicException) { return (null, null, "Połącz Instagram ponownie w zakładce Kanały."); }
    }

    public static string? Normalize(string kind, string? raw)
    {
        var v = (raw ?? "").Trim().TrimStart('@', '#').ToLowerInvariant();
        if (v.StartsWith("https://") || v.StartsWith("http://"))
            v = v.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        v = new string(v.Where(c => kind == "account" ? char.IsAsciiLetterOrDigit(c) || c is '.' or '_' : char.IsLetterOrDigit(c) || c == '_').ToArray());
        return v.Length is > 0 and <= 100 ? v : null;
    }

    public async Task<List<WatchItem>> ListAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WatchItems.AsNoTracking().OrderBy(w => w.Kind).ThenBy(w => w.Value).ToListAsync();
    }

    public async Task<(bool Ok, string Message)> AddAsync(string kind, string raw)
    {
        kind = kind == "hashtag" ? "hashtag" : "account";
        if (Normalize(kind, raw) is not { } value) return (false, kind == "account" ? "Wpisz nazwę konta, np. @studio.trenera." : "Wpisz hasztag, np. #trenerpersonalny.");
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.WatchItems.AnyAsync(w => w.Kind == kind && w.Value == value)) return (false, "To już jest na liście.");
        var count = await db.WatchItems.CountAsync(w => w.Kind == kind);
        if (count >= (kind == "account" ? MaxAccounts : MaxHashtags))
            return (false, kind == "account" ? $"Najwyżej {MaxAccounts} kont." : $"Najwyżej {MaxHashtags} hasztagów (Meta pozwala na 30 różnych w 7 dni).");
        var item = new WatchItem { Kind = kind, Value = value };
        db.WatchItems.Add(item);
        await db.SaveChangesAsync();
        var (ok, msg) = await SyncOneAsync(item.Id);
        return ok ? (true, kind == "account" ? $"Obserwujesz @{value}." : $"Obserwujesz #{value}.") : (true, $"Dodano, ale: {msg}");
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.WatchItems.Where(w => w.Id == id).ExecuteDeleteAsync();
    }

    public async Task<(bool Ok, string Message)> SyncOneAsync(int id, CancellationToken ct = default)
    {
        var (igId, token, error) = await ConnectionAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var w = await db.WatchItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return (false, "Nie ma takiej pozycji.");
        if (error is not null) { w.LastError = error; await db.SaveChangesAsync(ct); return (false, error); }
        try
        {
            if (w.Kind == "account")
            {
                var p = await client.ProfileAsync(igId!, token!, w.Value, ct);
                (w.Name, w.Bio, w.PictureUrl, w.Followers, w.MediaCount) = (p.Name, Clip(p.Bio, 500), p.Picture, p.Followers, p.MediaCount);
                w.MediaJson = JsonSerializer.Serialize(p.Media, Json);
                var day = DateOnly.FromDateTime(InstagramRules.ToWarsaw(DateTime.UtcNow));
                var row = await db.WatchDays.FirstOrDefaultAsync(d => d.WatchId == w.Id && d.Day == day, ct);
                if (row is null) db.WatchDays.Add(new WatchDay { WatchId = w.Id, Day = day, Followers = p.Followers });
                else row.Followers = p.Followers;
            }
            else
            {
                w.HashtagId ??= await client.HashtagIdAsync(igId!, token!, w.Value, ct);
                var top = await client.HashtagMediaAsync(w.HashtagId, igId!, token!, recent: false, ct);
                w.MediaJson = JsonSerializer.Serialize(top.Take(25).ToList(), Json);
            }
            w.SyncedAt = DateTime.UtcNow;
            w.LastError = null;
            await db.SaveChangesAsync(ct);
            return (true, "Odświeżono.");
        }
        catch (InstagramApiException ex)
        {
            w.LastError = Clip(ex.Message, 500);
            await db.SaveChangesAsync(ct);
            return (false, ex.Message);
        }
    }

    /// <summary>Raz dziennie (z pętli w tle) — wszystkie pozycje.</summary>
    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        foreach (var w in await ListAsync())
        {
            var (ok, msg) = await SyncOneAsync(w.Id, ct);
            if (!ok) logger.LogInformation("Obserwacja {Kind} {Value}: {Message}", w.Kind, w.Value, msg);
        }
    }

    public static List<WatchClient.Media> MediaOf(WatchItem w)
    {
        try { return JsonSerializer.Deserialize<List<WatchClient.Media>>(w.MediaJson, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Średnie zaangażowanie (polubienia + komentarze / obserwujący), posty na tydzień, najlepsze posty.</summary>
    public static Summary Summarize(List<WatchClient.Media> media, int? followers, int? followersWeekAgo)
    {
        double? engagement = followers is > 0 && media.Count > 0 ? Math.Round(media.Average(m => 100.0 * (m.Likes + m.Comments) / followers.Value), 2) : null;
        var perWeek = 0.0;
        if (media.Count > 1)
        {
            var span = (media.Max(m => m.At) - media.Min(m => m.At)).TotalDays;
            perWeek = span > 0 ? Math.Round(media.Count / span * 7, 1) : media.Count;
        }
        return new Summary(engagement, perWeek, followers is int f && followersWeekAgo is int f7 ? f - f7 : null,
            media.OrderByDescending(m => m.Likes + m.Comments).Take(3).ToList());
    }

    public async Task<Dictionary<int, int>> FollowersWeekAgoAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var day = DateOnly.FromDateTime(InstagramRules.ToWarsaw(DateTime.UtcNow)).AddDays(-7);
        return (await db.WatchDays.AsNoTracking().Where(d => d.Day <= day).ToListAsync())
            .GroupBy(d => d.WatchId).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.Day).First().Followers);
    }

    /// <summary>Wnioski AI dla pozycji: co działa u nich i 3 pomysły dla Hantly (bez kopiowania).</summary>
    public async Task<(string? Text, string? Error)> InsightsAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var w = await db.WatchItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return (null, "Nie ma takiej pozycji.");
        var media = MediaOf(w);
        if (media.Count == 0) return (null, "Brak postów do analizy — odśwież najpierw.");
        var sb = new StringBuilder();
        sb.AppendLine(w.Kind == "account" ? $"Konto: @{w.Value}, obserwujący: {w.Followers?.ToString() ?? "?"}." : $"Hasztag: #{w.Value} (najpopularniejsze posty).");
        foreach (var m in media.OrderByDescending(m => m.Likes + m.Comments).Take(12))
            sb.AppendLine($"- {m.Type}, {m.Likes} polubień, {m.Comments} komentarzy, {InstagramRules.ToWarsaw(m.At):ddd HH:mm}: „{DmService.Scrub(InstagramGraphics.Clean(m.Caption, 160))}”");
        var (text, error) = await ai.WatchAsync(sb.ToString(), ct);
        if (text is not null)
        {
            w.Insights = Clip(text, 4000);
            await db.SaveChangesAsync(ct);
        }
        return (text, error);
    }

    private static string? Clip(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}
