using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// TikTok i YouTube przez API: klucze aplikacji, logowanie (OAuth), odświeżanie tokenów i publikacja filmu.
/// Dopóki platforma nie zatwierdzi aplikacji (audyt TikToka, weryfikacja projektu Google), filmy wychodzą prywatnie —
/// prywatność ustawia się w panelu, a kanał ręczny można zostawić obok.
/// </summary>
public class VideoChannelService(
    IDbContextFactory<PortalDbContext> dbFactory,
    TikTokClient tiktok,
    YouTubeClient youtube,
    SiteSettingsService settings,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<VideoChannelService> logger)
{
    public static readonly string[] TikTokPrivacy = ["SELF_ONLY", "MUTUAL_FOLLOW_FRIENDS", "FOLLOWER_OF_CREATOR", "PUBLIC_TO_EVERYONE"];
    public static readonly string[] YouTubePrivacy = ["private", "unlisted", "public"];
    private const long MaxVideoBytes = 256L * 1024 * 1024;

    private IDataProtector Secrets => dataProtection.CreateProtector("instagram");
    private string PortalUrl => PortalUrls.Public(config);
    public string RedirectUri(SocialPlatform p) => $"{PortalUrl}/panel/social/oauth/{(p == SocialPlatform.TikTok ? "tiktok" : "youtube")}";

    private string? Unprotect(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); } catch (CryptographicException) { return null; }
    }

    public sealed record Keys(string TikTokKey, bool TikTokSecret, string GoogleId, bool GoogleSecret, string TikTokPrivacyLevel, string YouTubePrivacyLevel);

    public async Task<Keys> KeysAsync()
    {
        var s = await settings.GetAllAsync(K.TikTokClientKey, K.TikTokClientSecret, K.GoogleClientId, K.GoogleClientSecret, K.TikTokPrivacy, K.YouTubePrivacy);
        return new Keys(s[K.TikTokClientKey].Trim(), s[K.TikTokClientSecret].Length > 0, s[K.GoogleClientId].Trim(), s[K.GoogleClientSecret].Length > 0,
            TikTokPrivacy.Contains(s[K.TikTokPrivacy]) ? s[K.TikTokPrivacy] : "SELF_ONLY",
            YouTubePrivacy.Contains(s[K.YouTubePrivacy]) ? s[K.YouTubePrivacy] : "private");
    }

    public async Task SaveKeysAsync(SocialPlatform p, string id, string? secret)
    {
        if (p == SocialPlatform.TikTok)
        {
            await settings.SetAsync(K.TikTokClientKey, id.Trim());
            if (!string.IsNullOrWhiteSpace(secret)) await settings.SetAsync(K.TikTokClientSecret, Secrets.Protect(secret.Trim()));
        }
        else
        {
            await settings.SetAsync(K.GoogleClientId, id.Trim());
            if (!string.IsNullOrWhiteSpace(secret)) await settings.SetAsync(K.GoogleClientSecret, Secrets.Protect(secret.Trim()));
        }
    }

    public async Task SavePrivacyAsync(string tiktokPrivacy, string youtubePrivacy)
    {
        if (TikTokPrivacy.Contains(tiktokPrivacy)) await settings.SetAsync(K.TikTokPrivacy, tiktokPrivacy);
        if (YouTubePrivacy.Contains(youtubePrivacy)) await settings.SetAsync(K.YouTubePrivacy, youtubePrivacy);
    }

    private async Task<(string Id, string Secret)?> AppAsync(SocialPlatform p)
    {
        var s = await settings.GetAllAsync(K.TikTokClientKey, K.TikTokClientSecret, K.GoogleClientId, K.GoogleClientSecret);
        var (id, secret) = p == SocialPlatform.TikTok ? (s[K.TikTokClientKey].Trim(), Unprotect(s[K.TikTokClientSecret])) : (s[K.GoogleClientId].Trim(), Unprotect(s[K.GoogleClientSecret]));
        return id.Length > 0 && secret is not null ? (id, secret) : null;
    }

    // ── Logowanie ────────────────────────────────────────────────────

    public async Task<(string? Url, string? Error)> AuthorizeUrlAsync(SocialPlatform p)
    {
        if (await AppAsync(p) is not { } app) return (null, p == SocialPlatform.TikTok ? "Wpisz Client key i Client secret aplikacji TikTok." : "Wpisz Client ID i Client secret z Google Cloud.");
        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        await settings.SetAsync(K.VideoOAuthState, $"{(int)p}:{state}:{DateTime.UtcNow.Ticks}");
        return (p == SocialPlatform.TikTok ? tiktok.AuthorizeUrl(app.Id, RedirectUri(p), state) : YouTubeClient.AuthorizeUrl(app.Id, RedirectUri(p), state), null);
    }

    public async Task<(bool Ok, string Message)> CallbackAsync(SocialPlatform p, string? code, string? state, string? error, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(error)) return (false, $"Połączenie przerwane: {error}.");
        var saved = (await settings.GetAsync(K.VideoOAuthState)).Split(':');
        await settings.SetAsync(K.VideoOAuthState, "");
        if (saved.Length != 3 || saved[0] != ((int)p).ToString() || saved[1] != state || !long.TryParse(saved[2], out var ticks)
            || DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > TimeSpan.FromMinutes(15))
            return (false, "Link logowania wygasł albo jest nieprawidłowy — spróbuj jeszcze raz.");
        if (string.IsNullOrEmpty(code)) return (false, "Brak kodu logowania.");
        if (await AppAsync(p) is not { } app) return (false, "Brak kluczy aplikacji.");
        try
        {
            OAuthTokens tokens;
            string name, external;
            string? picture;
            int followers = 0;
            if (p == SocialPlatform.TikTok)
            {
                tokens = await tiktok.ExchangeAsync(app.Id, app.Secret, code, RedirectUri(p), ct);
                (name, picture) = await tiktok.UserAsync(tokens.Access, ct);
                external = name;
            }
            else
            {
                tokens = await youtube.ExchangeAsync(app.Id, app.Secret, code, RedirectUri(p), ct);
                if (tokens.Refresh is null) return (false, "Google nie dał tokenu odświeżania — usuń dostęp aplikacji w koncie Google i połącz ponownie.");
                (external, name, picture, followers) = await youtube.ChannelAsync(tokens.Access, ct);
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ch = await db.SocialChannels.FirstOrDefaultAsync(c => c.Platform == p && c.Api, ct);
            if (ch is null) db.SocialChannels.Add(ch = new SocialChannel { Platform = p, Api = true, Default = false });
            ch.Name = name;
            ch.Handle = p == SocialPlatform.TikTok ? name : null;
            ch.ExternalId = external;
            ch.PictureUrl = picture;
            ch.Followers = followers > 0 ? followers : ch.Followers;
            ch.ProfileUrl = p == SocialPlatform.TikTok ? $"https://www.tiktok.com/@{name}" : $"https://www.youtube.com/channel/{external}";
            Store(ch, tokens);
            ch.ConnectedAt = DateTime.UtcNow;
            ch.LastError = null;
            ch.Enabled = true;
            await db.SaveChangesAsync(ct);
            return (true, $"Połączono {SocialPlatforms.Get(p).Label}: {name}.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    private void Store(SocialChannel ch, OAuthTokens t)
    {
        ch.Token = Secrets.Protect(JsonSerializer.Serialize(t));
        ch.TokenExpiresAt = t.ExpiresAt;
        ch.TokenRefreshedAt = DateTime.UtcNow;
    }

    /// <summary>Ważny token dostępu (odświeżany, gdy wygasa). Zapisuje nowy token w kanale (śledzonym przez db).</summary>
    private async Task<string> AccessAsync(SocialChannel ch, CancellationToken ct)
    {
        var t = Unprotect(ch.Token) is { } raw ? JsonSerializer.Deserialize<OAuthTokens>(raw) : null;
        if (t is null) throw new InstagramApiException("Kanał nie jest połączony — połącz go ponownie w Kanałach.", 190);
        if (t.ExpiresAt > DateTime.UtcNow.AddMinutes(5)) return t.Access;
        if (t.Refresh is null || await AppAsync(ch.Platform) is not { } app) throw new InstagramApiException("Token wygasł — połącz kanał ponownie.", 190);
        var fresh = ch.Platform == SocialPlatform.TikTok
            ? await tiktok.RefreshAsync(app.Id, app.Secret, t.Refresh, ct)
            : await youtube.RefreshAsync(app.Id, app.Secret, t.Refresh, ct);
        Store(ch, fresh);
        return fresh.Access;
    }

    private async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("video-download");
        http.Timeout = TimeSpan.FromMinutes(5);
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new InstagramApiException($"Nie udało się pobrać filmu (HTTP {(int)resp.StatusCode}).");
        if (resp.Content.Headers.ContentLength > MaxVideoBytes) throw new InstagramApiException("Film jest za duży (najwięcej 256 MB).");
        var data = await resp.Content.ReadAsByteArrayAsync(ct);
        return data.Length <= MaxVideoBytes ? data : throw new InstagramApiException("Film jest za duży (najwięcej 256 MB).");
    }

    /// <summary>
    /// Publikacja na TikToku albo YouTube (kanał i cel śledzone przez db wywołującego). TikTok: wysyłka → „publikuję” →
    /// kolejne przebiegi sprawdzają stan; YouTube: wysyłka od razu daje film.
    /// </summary>
    public async Task PublishAsync(SocialPostTarget t, SocialChannel ch, InstagramPost post, string caption, string? videoUrl, CancellationToken ct)
    {
        var token = await AccessAsync(ch, ct);
        var keys = await KeysAsync();
        if (ch.Platform == SocialPlatform.TikTok)
        {
            if (t.ContainerId is null)
            {
                if (videoUrl is null) throw new InstagramApiException("TikTok przyjmuje tylko film.");
                var video = await DownloadAsync(videoUrl, ct);
                var title = caption.Length > 2200 ? caption[..2200] : caption;
                t.ContainerId = await tiktok.UploadAsync(token, video, title, keys.TikTokPrivacyLevel, ct);
                t.Status = SocialTargetStatus.Publishing;
            }
            var (status, postId, error) = await tiktok.StatusAsync(token, t.ContainerId, ct);
            if (status == "PUBLISH_COMPLETE")
            {
                t.Status = SocialTargetStatus.Published;
                t.PublishedAt = DateTime.UtcNow;
                t.ExternalId = postId ?? t.ContainerId;
                t.Permalink = postId is not null && ch.Handle is not null ? $"https://www.tiktok.com/@{ch.Handle}/video/{postId}" : ch.ProfileUrl;
            }
            else if (status == "FAILED")
            {
                t.ContainerId = null;
                throw new InstagramApiException($"TikTok nie opublikował filmu: {error ?? "nieznany powód"}.");
            }
            return;
        }
        if (videoUrl is null) throw new InstagramApiException("YouTube przyjmuje tylko film.");
        var bytes = await DownloadAsync(videoUrl, ct);
        var lines = caption.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var ytTitle = string.IsNullOrWhiteSpace(post.Title) ? InstagramGraphics.Clean(lines.FirstOrDefault(), 90) : post.Title;
        var tags = InstagramRules.Hashtags(caption).Select(h => h.TrimStart('#')).ToList();
        var id = await youtube.UploadAsync(token, bytes, ytTitle.Contains("#Shorts", StringComparison.OrdinalIgnoreCase) ? ytTitle : $"{ytTitle} #Shorts",
            caption, tags, keys.YouTubePrivacyLevel, ct);
        t.ExternalId = id;
        t.Status = SocialTargetStatus.Published;
        t.PublishedAt = DateTime.UtcNow;
        t.Permalink = $"https://youtube.com/shorts/{id}";
        logger.LogInformation("YouTube: wysłano film {Id} ({Privacy}).", id, keys.YouTubePrivacyLevel);
    }
}
