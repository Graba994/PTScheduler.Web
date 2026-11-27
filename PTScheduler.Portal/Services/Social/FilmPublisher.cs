using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services.Studio;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Gotowy film ze Studia reklam → post (Rolka) w planie mediów społecznościowych: Instagram, Facebook, Threads przez API,
/// TikTok i YouTube Shorts jako paczki ręczne. Meta pobiera plik z publicznego, nieodgadnionego adresu
/// /media/film/{id}/{podpis}.mp4 (podpis HMAC — bez zmian w danych Studia). Post trafia do planu jako szkic: decyzję
/// o terminie i kanałach podejmuje człowiek.
/// </summary>
public class FilmPublisher(
    IDbContextFactory<PortalDbContext> dbFactory,
    StudioVideos videos,
    InstagramService ig,
    SocialService social,
    InstagramAi ai,
    SiteSettingsService settings,
    IConfiguration config)
{
    public static string SourceOf(int videoId) => $"film:{videoId}";

    private async Task<byte[]> KeyAsync()
    {
        var raw = await settings.GetAsync(K.FilmMediaKey);
        if (!string.IsNullOrWhiteSpace(raw)) return Convert.FromBase64String(raw);
        var key = RandomNumberGenerator.GetBytes(32);
        await settings.SetAsync(K.FilmMediaKey, Convert.ToBase64String(key));
        return key;
    }

    public static string Sign(byte[] key, int id, string slug) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"film:{id}:{slug}")))[..32];

    public async Task<string> PublicUrlAsync(StudioVideo v) =>
        $"{PortalUrls.Public(config)}/media/film/{v.Id}/{Sign(await KeyAsync(), v.Id, v.Slug)}.mp4";

    /// <summary>Plik filmu dla publicznego adresu — tylko z poprawnym podpisem i gotowym filmem.</summary>
    public async Task<string?> FileAsync(int id, string signature)
    {
        if (signature.Length != 32) return null;
        var v = await videos.GetAsync(id);
        if (v is null || !videos.HasVideo(v) || v.Status is not (StudioVideoStatus.ReadyToPublish or StudioVideoStatus.Published)) return null;
        var expected = Sign(await KeyAsync(), v.Id, v.Slug);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()))
            ? videos.VideoFile(v) : null;
    }

    /// <summary>Post utworzony już z tego filmu (jeśli jest).</summary>
    public async Task<InstagramPost?> PostAsync(int videoId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var source = SourceOf(videoId);
        return await db.InstagramPosts.AsNoTracking().Where(p => p.Source == source).OrderByDescending(p => p.Id).FirstOrDefaultAsync();
    }

    /// <summary>Opis posta z opisu filmu: tekst, hasztagi, informacja o Garminie i „{link}” (Instagram: „link w bio”).</summary>
    public static string Caption(StudioVideo v)
    {
        var sb = new StringBuilder(v.Description.Trim());
        if (!sb.ToString().Contains(LinkService.Placeholder, StringComparison.OrdinalIgnoreCase)) sb.Append("\n\nSprawdź Hantly: ").Append(LinkService.Placeholder);
        var tags = StudioVideos.List(v.TagsJson).Select(t => "#" + t.TrimStart('#').Replace(" ", "")).Where(t => t.Length > 1).Distinct().ToList();
        if (tags.Count > 0 && !v.Description.Contains('#')) sb.Append("\n\n").Append(string.Join(" ", tags));
        if (v.Garmin && (v.Title + v.Description).Contains("Garmin", StringComparison.OrdinalIgnoreCase))
            sb.Append("\n\n").Append(StudioService.GarminAttribution);
        var text = sb.ToString();
        return text.Length > InstagramRules.MaxCaption ? text[..InstagramRules.MaxCaption] : text;
    }

    public async Task<(bool Ok, string Message, int? PostId)> ToSocialAsync(int videoId)
    {
        var v = await videos.GetAsync(videoId);
        if (v is null) return (false, "Nie ma takiego filmu.", null);
        if (v.Status is not (StudioVideoStatus.ReadyToPublish or StudioVideoStatus.Published) || !videos.HasVideo(v))
            return (false, "Najpierw zaakceptuj gotowe wideo.", null);
        if (await PostAsync(videoId) is { } existing) return (true, "Ten film jest już w planie.", existing.Id);

        var caption = Caption(v);
        var postId = await ig.CreatePostAsync(InstagramPostKind.Reel, v.Title, "funkcja", SourceOf(videoId));
        await ig.SavePostAsync(postId, v.Title, caption, InstagramPostKind.Reel, "funkcja", true);
        var added = await ig.AddVideoAsync(postId, await PublicUrlAsync(v));
        if (!added.Ok) return (false, added.Message, postId);

        // Wszystkie włączone kanały (do odznaczenia w edytorze); Instagram — jeśli połączony.
        var channels = await social.ChannelsAsync(enabledOnly: true);
        await social.SetTargetsAsync(postId, await ig.AccountAsync() is not null, channels.Select(c => c.Id).ToList());

        // Krótsze platformy (Threads, X…) — opis dopasowany przez AI, o ile działa (do poprawy w edytorze).
        var adapted = 0;
        if (await ai.UnavailableAsync() is null)
        {
            var post = new InstagramPost { Id = postId, Title = v.Title, Caption = caption };
            foreach (var t in await social.TargetsAsync(postId))
            {
                if (SocialPlatforms.Get(t.Channel!.Platform).MaxText >= caption.Length) continue;
                var (text, _) = await social.AdaptCaptionAsync(t.Channel.Platform, post);
                if (text is null) continue;
                await social.SetCaptionAsync(t.Id, text);
                adapted++;
            }
        }
        var count = channels.Count + (await ig.AccountAsync() is not null ? 1 : 0);
        var note = adapted > 0 ? $" Skrócone opisy (AI): {adapted}." : "";
        return (true, $"Film jest w planie jako szkic (kanałów: {count}).{note} Ustaw termin i opublikuj.", postId);
    }
}
