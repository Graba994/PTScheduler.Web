using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Instagram marki w Portalu: połączenie konta, posty (szkice, plan, publikacja), grafiki, statystyki,
/// komentarze i autopilot. Sieć — <see cref="InstagramClient"/>, teksty AI — <see cref="InstagramAi"/>,
/// grafiki — <see cref="InstagramGraphics"/>. Usługa w tle: <see cref="InstagramBackgroundService"/>.
/// </summary>
public class InstagramService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    InstagramClient client,
    InstagramAi ai,
    PTScheduler.Portal.Services.Social.SocialService social,
    EmailService email,
    IDataProtectionProvider dataProtection,
    IConfiguration config,
    ILogger<InstagramService> logger)
{
    private IDataProtector Secrets => dataProtection.CreateProtector("instagram");
    public string PortalUrl => PortalUrls.Public(config);
    public string RedirectUri => $"{PortalUrl}/panel/instagram/callback";
    public string AssetUrl(InstagramAsset a) => a.Kind == "video" ? a.ExternalUrl ?? "" : $"{PortalUrl}/media/ig/{a.Token}.jpg";

    // ── Konfiguracja i połączenie ─────────────────────────────────────

    public sealed record AppConfig(string AppId, bool HasSecret);

    public async Task<AppConfig> AppAsync()
    {
        var s = await settings.GetAllAsync(K.InstagramAppId, K.InstagramAppSecret);
        return new(s[K.InstagramAppId].Trim(), !string.IsNullOrWhiteSpace(s[K.InstagramAppSecret]));
    }

    public async Task SaveAppAsync(string appId, string? secret)
    {
        var values = new Dictionary<string, string> { [K.InstagramAppId] = appId.Trim() };
        if (!string.IsNullOrWhiteSpace(secret)) values[K.InstagramAppSecret] = Secrets.Protect(secret.Trim());
        await settings.SetManyAsync(values);
    }

    private async Task<string?> SecretAsync()
    {
        var raw = await settings.GetAsync(K.InstagramAppSecret);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return Secrets.Unprotect(raw); } catch (CryptographicException) { return null; }
    }

    public async Task<InstagramAccount?> AccountAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.InstagramAccounts.AsNoTracking().OrderBy(a => a.Id).FirstOrDefaultAsync();
    }

    private string? Token(InstagramAccount a)
    {
        try { return Secrets.Unprotect(a.Token); } catch (CryptographicException) { return null; }
    }

    /// <summary>Konto i odszyfrowany token — albo powód, dlaczego nie da się działać.</summary>
    private async Task<(InstagramAccount? Account, string? Token, string? Error)> ConnectedAsync()
    {
        var a = await AccountAsync();
        if (a is null) return (null, null, "Instagram nie jest połączony.");
        var token = Token(a);
        if (token is null) return (a, null, "Nie da się odczytać tokenu — połącz konto ponownie.");
        if (a.TokenExpiresAt < DateTime.UtcNow) return (a, null, "Token Instagrama wygasł — połącz konto ponownie.");
        client.Provider = a.Provider;
        return (a, token, null);
    }

    /// <summary>Adres logowania do Instagrama (z jednorazowym „state” na 15 minut).</summary>
    public async Task<(string? Url, string? Error)> ConnectUrlAsync()
    {
        var app = await AppAsync();
        if (app.AppId.Length == 0 || !app.HasSecret) return (null, "Najpierw wpisz identyfikator i klucz aplikacji Instagram (krok 2).");
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await settings.SetAsync(K.InstagramAuthState, $"{state}|{DateTime.UtcNow:O}");
        return (InstagramClient.AuthorizeUrl(app.AppId, RedirectUri, state), null);
    }

    /// <summary>Powrót z logowania: kod → token krótki → długi (60 dni) → profil. Zwraca komunikat do pokazania.</summary>
    public async Task<(bool Ok, string Message)> CallbackAsync(string? code, string? state, string? error)
    {
        var saved = (await settings.GetAsync(K.InstagramAuthState)).Split('|');
        await settings.SetAsync(K.InstagramAuthState, "");
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return (false, "Logowanie do Instagrama przerwane.");
        if (saved.Length != 2 || saved[0] != state || !DateTime.TryParse(saved[1], null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) || at < DateTime.UtcNow.AddMinutes(-15))
            return (false, "Link logowania wygasł — kliknij „Połącz z Instagramem” jeszcze raz.");
        var app = await AppAsync();
        var secret = await SecretAsync();
        if (secret is null) return (false, "Brak klucza aplikacji Instagram.");
        try
        {
            var (shortToken, _) = await client.ExchangeCodeAsync(app.AppId, secret, RedirectUri, code);
            var (token, expires) = await client.LongLivedAsync(secret, shortToken);
            return await SaveConnectionAsync(token, expires);
        }
        catch (InstagramApiException ex)
        {
            logger.LogWarning("Instagram: połączenie nie powiodło się: {Error}", ex.Message);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Połączenie wklejonym tokenem (wygenerowanym w panelu Meta: Instagram → API setup → Generate token).
    /// Prostsze niż logowanie — nie trzeba ustawiać adresu powrotu.
    /// </summary>
    public async Task<(bool Ok, string Message)> ConnectWithTokenAsync(string token)
    {
        token = token.Trim();
        if (token.Length < 20) return (false, "To nie wygląda na token Instagrama.");
        try
        {
            // Krótki token (1 h) da się wymienić na długi, jeśli znamy klucz aplikacji; długi zostaje jak jest.
            var expires = DateTime.UtcNow.AddDays(59);
            if (await SecretAsync() is { } secret)
            {
                try { (token, expires) = await client.LongLivedAsync(secret, token); }
                catch (InstagramApiException ex) when (!ex.TokenInvalid) { }
            }
            return await SaveConnectionAsync(token, expires);
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    private async Task<(bool Ok, string Message)> SaveConnectionAsync(string token, DateTime expires)
    {
        var me = await client.MeAsync(token);
        if (string.IsNullOrEmpty(me.UserId)) return (false, "Instagram nie podał identyfikatora konta. Czy to konto profesjonalne (Firma albo Twórca)?");
        await using var db = await dbFactory.CreateDbContextAsync();
        var a = await db.InstagramAccounts.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (a is null) db.InstagramAccounts.Add(a = new InstagramAccount());
        else if (a.IgUserId != me.UserId)
        {
            // Inne konto niż wcześniej — stare statystyki i komentarze nie pasują.
            await db.InstagramMedia.ExecuteDeleteAsync();
            await db.InstagramComments.ExecuteDeleteAsync();
            await db.InstagramDaily.ExecuteDeleteAsync();
        }
        a.IgUserId = me.UserId;
        a.Provider = "instagram";
        a.Username = me.Username;
        a.Name = me.Name;
        a.PictureUrl = me.Picture;
        a.Followers = me.Followers;
        a.Follows = me.Follows;
        a.MediaCount = me.MediaCount;
        a.Token = Secrets.Protect(token);
        a.TokenExpiresAt = expires;
        a.TokenRefreshedAt = DateTime.UtcNow;
        a.ConnectedAt = DateTime.UtcNow;
        a.LastError = null;
        await db.SaveChangesAsync();
        _ = SafeAsync(() => SyncAsync(), "pierwsza synchronizacja");
        return (true, $"Połączono z @{me.Username}.");
    }

    public async Task DisconnectAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramAccounts.ExecuteDeleteAsync();
    }

    /// <summary>Odnawia token, gdy zostało mniej niż 20 dni. Zwraca true, gdy odnowił.</summary>
    public async Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default)
    {
        var (a, token, _) = await ConnectedAsync();
        if (a is null || token is null || a.Provider == "facebook" || !InstagramRules.TokenNeedsRefresh(a.TokenExpiresAt, a.TokenRefreshedAt, DateTime.UtcNow)) return false;
        try
        {
            var (fresh, expires) = await client.RefreshAsync(token, ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.InstagramAccounts.Where(x => x.Id == a.Id).ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Token, Secrets.Protect(fresh)).SetProperty(x => x.TokenExpiresAt, expires)
                .SetProperty(x => x.TokenRefreshedAt, DateTime.UtcNow).SetProperty(x => x.LastError, (string?)null), ct);
            return true;
        }
        catch (InstagramApiException ex)
        {
            await NoteErrorAsync(a.Id, ex.Message);
            return false;
        }
    }

    private async Task NoteErrorAsync(int accountId, string? error)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramAccounts.Where(x => x.Id == accountId).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastError, error));
    }

    public async Task<(int Used, int Total)?> PublishingLimitAsync()
    {
        var (a, token, _) = await ConnectedAsync();
        if (a is null || token is null) return null;
        try { return await client.PublishingLimitAsync(token, a.IgUserId); } catch (InstagramApiException) { return null; }
    }

    // ── Ustawienia autopilota ─────────────────────────────────────────

    public sealed record Autopilot(string Mode, int PostsPerWeek, HashSet<int> Days, TimeOnly Time, List<string> Pillars, string Voice, string Hashtags, string CommentMode);

    public async Task<Autopilot> AutopilotAsync()
    {
        var s = await settings.GetAllAsync(K.InstagramAutopilot, K.InstagramPostsPerWeek, K.InstagramPostDays, K.InstagramPostTime,
            K.InstagramPillars, K.InstagramVoice, K.InstagramHashtags, K.InstagramCommentMode);
        var pillars = s[K.InstagramPillars].Split(',', StringSplitOptions.RemoveEmptyEntries).Where(p => InstagramRules.FindPillar(p) is not null).ToList();
        return new(s[K.InstagramAutopilot] is "drafts" or "auto" ? s[K.InstagramAutopilot] : "off",
            int.TryParse(s[K.InstagramPostsPerWeek], out var n) ? Math.Clamp(n, 1, 7) : 3,
            InstagramRules.ParseDays(s[K.InstagramPostDays]), InstagramRules.ParseTime(s[K.InstagramPostTime]),
            pillars.Count > 0 ? pillars : InstagramRules.Pillars.Select(p => p.Key).ToList(),
            s[K.InstagramVoice], s[K.InstagramHashtags], s[K.InstagramCommentMode] is "off" or "auto" ? s[K.InstagramCommentMode] : "suggest");
    }

    public Task SaveAutopilotAsync(Autopilot a) => settings.SetManyAsync(new Dictionary<string, string>
    {
        [K.InstagramAutopilot] = a.Mode,
        [K.InstagramPostsPerWeek] = a.PostsPerWeek.ToString(),
        [K.InstagramPostDays] = string.Join(',', a.Days.Order()),
        [K.InstagramPostTime] = a.Time.ToString("HH:mm"),
        [K.InstagramPillars] = string.Join(',', a.Pillars),
        [K.InstagramVoice] = a.Voice.Trim(),
        [K.InstagramHashtags] = string.Join(' ', InstagramRules.ParseTags(a.Hashtags)),
        [K.InstagramCommentMode] = a.CommentMode,
    });

    // ── Posty ─────────────────────────────────────────────────────────

    public async Task<List<InstagramPost>> PostsAsync(Func<IQueryable<InstagramPost>, IQueryable<InstagramPost>>? filter = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        IQueryable<InstagramPost> q = db.InstagramPosts.AsNoTracking();
        if (filter is not null) q = filter(q);
        var list = await q.ToListAsync();
        await AttachAssetsAsync(db, list);
        return list;
    }

    public async Task<InstagramPost?> PostAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = await db.InstagramPosts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is not null) await AttachAssetsAsync(db, [p]);
        return p;
    }

    /// <summary>Pliki postów bez samych bajtów (podgląd idzie przez /media/ig/…).</summary>
    private static async Task AttachAssetsAsync(PortalDbContext db, List<InstagramPost> posts)
    {
        var ids = posts.Select(p => p.Id).ToList();
        if (ids.Count == 0) return;
        var assets = await db.InstagramAssets.AsNoTracking().Where(a => ids.Contains(a.PostId))
            .Select(a => new InstagramAsset
            {
                Id = a.Id, PostId = a.PostId, Order = a.Order, Kind = a.Kind, Token = a.Token, ExternalUrl = a.ExternalUrl,
                Width = a.Width, Height = a.Height, SizeBytes = a.SizeBytes, TemplateJson = a.TemplateJson, CreatedAt = a.CreatedAt
            }).ToListAsync();
        var byPost = assets.GroupBy(a => a.PostId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.Order).ToList());
        foreach (var p in posts) p.Assets = byPost.GetValueOrDefault(p.Id) ?? [];
    }

    public async Task<int> CreatePostAsync(InstagramPostKind kind, string title = "", string pillar = "", string source = "manual")
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = new InstagramPost { Kind = kind, Title = title, Pillar = pillar, Source = source, ShareToFeed = true };
        db.InstagramPosts.Add(p);
        await db.SaveChangesAsync();
        await social.ApplyDefaultsAsync(p.Id);
        return p.Id;
    }

    /// <summary>Zapis pól posta (bez zmiany statusu publikacji).</summary>
    public async Task SavePostAsync(int id, string title, string caption, InstagramPostKind kind, string pillar, bool shareToFeed)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = await db.InstagramPosts.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Nie ma takiego posta.");
        if (p.Status is InstagramPostStatus.Publishing or InstagramPostStatus.Published) return;
        p.Title = InstagramGraphics.Clean(title, 200);
        p.Caption = caption.Length > InstagramRules.MaxCaption ? caption[..InstagramRules.MaxCaption] : caption;
        p.Kind = kind;
        p.Pillar = pillar;
        p.ShareToFeed = shareToFeed;
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DeletePostAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramPosts.Where(p => p.Id == id && p.Status != InstagramPostStatus.Publishing).ExecuteDeleteAsync();
    }

    /// <summary>Czy post da się opublikować: pliki zgodne z rodzajem i opis w limitach. Pusta lista = tak.</summary>
    public static List<string> Problems(InstagramPost p)
    {
        if (!p.ToInstagram) return [];
        var list = InstagramRules.CaptionProblems(p.Caption);
        var images = p.Assets.Count(a => a.Kind == "image");
        var videos = p.Assets.Count(a => a.Kind == "video");
        switch (p.Kind)
        {
            case InstagramPostKind.Image when images != 1 || videos > 0: list.Add("Post ze zdjęciem potrzebuje dokładnie jednej grafiki."); break;
            case InstagramPostKind.Carousel when p.Assets.Count is < 2 or > InstagramRules.MaxCarousel: list.Add("Karuzela potrzebuje od 2 do 10 grafik."); break;
            case InstagramPostKind.Carousel when videos > 0: list.Add("Karuzela w Portalu obsługuje tylko grafiki."); break;
            case InstagramPostKind.Reel when videos != 1: list.Add("Rolka potrzebuje jednego filmu (adres MP4)."); break;
            case InstagramPostKind.Story when p.Assets.Count != 1: list.Add("Relacja potrzebuje jednej grafiki albo jednego filmu."); break;
        }
        if (p.Kind != InstagramPostKind.Story && string.IsNullOrWhiteSpace(p.Caption)) list.Add("Dodaj opis.");
        return list;
    }

    /// <summary>Instagram (jeśli wybrany) + wszystkie pozostałe wybrane kanały.</summary>
    public async Task<List<string>> AllProblemsAsync(InstagramPost p) => [.. Problems(p), .. await social.ProblemsAsync(p)];

    /// <summary>Planuje post na konkretny termin (UTC) albo najbliższy wolny termin z ustawień autopilota.</summary>
    public async Task<(bool Ok, string Message)> ScheduleAsync(int id, DateTime? atUtc)
    {
        var p = await PostAsync(id);
        if (p is null) return (false, "Nie ma takiego posta.");
        if (await AllProblemsAsync(p) is { Count: > 0 } problems) return (false, string.Join(" ", problems));
        if (atUtc is null)
        {
            var ap = await AutopilotAsync();
            var taken = (await PostsAsync(q => q.Where(x => x.Id != id && x.ScheduledAt != null && (x.Status == InstagramPostStatus.Scheduled || x.Status == InstagramPostStatus.ToApprove))))
                .Select(x => x.ScheduledAt!.Value);
            atUtc = InstagramRules.FreeSlots(DateTime.UtcNow, ap.Days, ap.Time, taken, 1, 60).FirstOrDefault();
            if (atUtc == default) return (false, "Brak wolnego terminu w najbliższych tygodniach — wybierz datę ręcznie.");
        }
        if (atUtc < DateTime.UtcNow.AddMinutes(-1)) return (false, "Ten termin już minął.");
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramPosts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.Status, InstagramPostStatus.Scheduled).SetProperty(x => x.ScheduledAt, atUtc)
            .SetProperty(x => x.Error, (string?)null).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        return (true, $"Zaplanowano na {InstagramRules.ToWarsaw(atUtc.Value):dd.MM HH:mm}.");
    }

    /// <summary>
    /// Przesunięcie terminu z kalendarza: zaplanowany — przez <see cref="ScheduleAsync"/> (sprawdza post),
    /// czekający na akceptację — tylko nowy termin (dalej czeka na akceptację).
    /// </summary>
    public async Task<(bool Ok, string Message)> RescheduleAsync(int id, DateTime atUtc)
    {
        var p = await PostAsync(id);
        if (p is null) return (false, "Nie ma takiego posta.");
        if (atUtc < DateTime.UtcNow.AddMinutes(1)) return (false, "Ten termin już minął — wybierz przyszły dzień.");
        if (p.Status == InstagramPostStatus.Scheduled) return await ScheduleAsync(id, atUtc);
        if (p.Status is not (InstagramPostStatus.ToApprove or InstagramPostStatus.Draft or InstagramPostStatus.Idea))
            return (false, "Opublikowanego albo publikowanego posta nie da się przesunąć.");
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramPosts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.ScheduledAt, atUtc).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        return (true, $"Nowy termin: {InstagramRules.ToWarsaw(atUtc):dd.MM HH:mm}{(p.Status == InstagramPostStatus.ToApprove ? " (dalej czeka na akceptację)" : " (szkic — zaplanuj go w edytorze)")}.");
    }

    /// <summary>Najlepsze pory dla każdej platformy (z ostatnich 90 dni): Instagram, Facebook, Threads.</summary>
    public async Task<Dictionary<string, List<InstagramRules.Slot>>> BestSlotsByPlatformAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = DateTime.UtcNow.AddDays(-90);
        var media = await db.InstagramMedia.AsNoTracking().Where(m => m.PostedAt >= since).ToListAsync();
        var igFollowers = await db.InstagramAccounts.AsNoTracking().Select(a => a.Followers).FirstOrDefaultAsync();
        var fbFollowers = await db.SocialChannels.AsNoTracking().Where(c => c.Platform == PTScheduler.Portal.Entities.SocialPlatform.Facebook).SumAsync(c => c.Followers ?? 0);
        var thFollowers = await db.SocialChannels.AsNoTracking().Where(c => c.Platform == PTScheduler.Portal.Entities.SocialPlatform.Threads).SumAsync(c => c.Followers ?? 0);
        var result = new Dictionary<string, List<InstagramRules.Slot>>();
        foreach (var (platform, followers) in new[] { ("instagram", igFollowers), ("facebook", fbFollowers), ("threads", thFollowers) })
        {
            var slots = InstagramRules.BestSlots(media.Where(m => m.Platform == platform), followers, minPosts: 1, take: 3);
            if (slots.Count > 0) result[platform] = slots;
        }
        return result;
    }

    /// <summary>Akceptacja posta od autopilota — trafia do planu na swój termin (albo najbliższy wolny).</summary>
    public async Task<(bool Ok, string Message)> ApproveAsync(int id)
    {
        var p = await PostAsync(id);
        if (p is null) return (false, "Nie ma takiego posta.");
        var at = p.ScheduledAt is { } s && s > DateTime.UtcNow.AddMinutes(5) ? s : (DateTime?)null;
        return await ScheduleAsync(id, at);
    }

    public async Task UnscheduleAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramPosts.Where(x => x.Id == id && (x.Status == InstagramPostStatus.Scheduled || x.Status == InstagramPostStatus.Failed || x.Status == InstagramPostStatus.ToApprove))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, InstagramPostStatus.Draft).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
    }

    /// <summary>Publikuje od razu (zdjęcia zwykle w kilka sekund, film — gdy Instagram go przetworzy).</summary>
    public async Task<(bool Ok, string Message)> PublishNowAsync(int id)
    {
        var p = await PostAsync(id);
        if (p is null) return (false, "Nie ma takiego posta.");
        if (await AllProblemsAsync(p) is { Count: > 0 } problems) return (false, string.Join(" ", problems));
        var igMessage = "";
        if (p.ToInstagram)
        {
            var (a, token, error) = await ConnectedAsync();
            if (error is not null) return (false, error);
            await StartPublishAsync(p, a!, token!, CancellationToken.None);
            await AdvanceAsync(id, a!, token!, CancellationToken.None);
        }
        else
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.InstagramPosts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, InstagramPostStatus.Publishing).SetProperty(x => x.ScheduledAt, DateTime.UtcNow).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        }
        await social.ProcessDueAsync(CancellationToken.None, id);
        var after = await PostAsync(id);
        // Nowy post od razu w statystykach i komentarzach (bez czekania na przebieg co 3 godziny).
        if (after?.Status == InstagramPostStatus.Published && p.ToInstagram) await SafeAsync(() => SyncAsync(), "synchronizacja po publikacji");
        var targets = await social.TargetsAsync(id);
        var others = targets.Count == 0 ? "" : " " + string.Join(", ", targets.Select(t => $"{PTScheduler.Portal.Services.Social.SocialPlatforms.Get(t.Channel!.Platform).Label}: {PTScheduler.Portal.Services.Social.SocialPlatforms.TargetLabel(t.Status).ToLowerInvariant()}")) + ".";
        if (p.ToInstagram)
            igMessage = after?.Status switch
            {
                InstagramPostStatus.Published => "Instagram: opublikowano.",
                InstagramPostStatus.Publishing => "Instagram przetwarza plik — post pojawi się za chwilę.",
                _ => $"Instagram: {after?.Error ?? "nie udało się"}."
            };
        var ok = after?.Status is InstagramPostStatus.Published or InstagramPostStatus.Publishing && targets.All(t => t.Status != SocialTargetStatus.Failed);
        return (ok, (igMessage + others).Trim());
    }

    // ── Pliki posta ───────────────────────────────────────────────────

    /// <summary>Wgrane zdjęcie / zrzut ekranu → JPEG w proporcjach Instagrama.</summary>
    public async Task<(bool Ok, string Message)> AddPhotoAsync(int postId, byte[] data, string theme = "grafit")
    {
        if (data.Length > 15 * 1024 * 1024) return (false, "Plik jest za duży (najwięcej 15 MB).");
        if (InstagramRules.ImageSize(data) is (0, 0)) return (false, "To nie jest zdjęcie (JPG, PNG albo WebP).");
        var p = await PostAsync(postId);
        if (p is null) return (false, "Nie ma takiego posta.");
        byte[] jpeg; int w, h;
        try { (jpeg, w, h) = InstagramGraphics.Photo(data, p.Kind == InstagramPostKind.Story, theme); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Instagram: nie udało się przygotować zdjęcia.");
            return (false, "Nie udało się odczytać zdjęcia — spróbuj w innym formacie (JPG albo PNG).");
        }
        await AddAssetAsync(postId, new InstagramAsset { Kind = "image", Data = jpeg, Width = w, Height = h, SizeBytes = jpeg.Length });
        return (true, "Dodano zdjęcie.");
    }

    public async Task<(bool Ok, string Message)> AddVideoAsync(int postId, string url)
    {
        url = url.Trim();
        // Własne pliki Portalu (np. film ze Studia) — zawsze; obce adresy tylko https.
        var own = url.StartsWith(PortalUrl + "/media/", StringComparison.OrdinalIgnoreCase);
        if (!own && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https")) return (false, "Podaj publiczny adres https do pliku MP4.");
        await AddAssetAsync(postId, new InstagramAsset { Kind = "video", ExternalUrl = url });
        return (true, "Dodano film.");
    }

    /// <summary>Grafika z szablonu (z zapamiętaną treścią, żeby dało się ją poprawić).</summary>
    public async Task<(bool Ok, string Message)> AddGraphicAsync(int postId, InstagramGraphics.Spec spec, byte[]? screenshot = null, int? replaceAssetId = null)
    {
        byte[] jpeg;
        try { jpeg = InstagramGraphics.Render(spec, screenshot); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Instagram: grafika z szablonu nie powstała.");
            return (false, "Nie udało się przygotować grafiki — skróć tekst i spróbuj ponownie.");
        }
        var (w, h) = InstagramGraphics.Pixels(spec.Size);
        var json = JsonSerializer.Serialize(spec);
        if (replaceAssetId is int rid)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var a = await db.InstagramAssets.FirstOrDefaultAsync(x => x.Id == rid && x.PostId == postId);
            if (a is null) return (false, "Nie ma takiej grafiki.");
            a.Data = jpeg; a.Width = w; a.Height = h; a.SizeBytes = jpeg.Length; a.TemplateJson = json;
            a.Token = Guid.NewGuid().ToString("N"); // nowy adres — Instagram nie weźmie starej wersji z pamięci
            await db.SaveChangesAsync();
            return (true, "Zaktualizowano grafikę.");
        }
        await AddAssetAsync(postId, new InstagramAsset { Kind = "image", Data = jpeg, Width = w, Height = h, SizeBytes = jpeg.Length, TemplateJson = json });
        return (true, "Dodano grafikę.");
    }

    public static InstagramGraphics.Spec? SpecOf(InstagramAsset a)
    {
        if (string.IsNullOrEmpty(a.TemplateJson)) return null;
        try { return JsonSerializer.Deserialize<InstagramGraphics.Spec>(a.TemplateJson); } catch (JsonException) { return null; }
    }

    private async Task AddAssetAsync(int postId, InstagramAsset asset)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        asset.PostId = postId;
        asset.Order = (await db.InstagramAssets.Where(a => a.PostId == postId).MaxAsync(a => (int?)a.Order) ?? -1) + 1;
        db.InstagramAssets.Add(asset);
        await db.InstagramPosts.Where(p => p.Id == postId).ExecuteUpdateAsync(u => u.SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    public async Task RemoveAssetAsync(int assetId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramAssets.Where(a => a.Id == assetId).ExecuteDeleteAsync();
    }

    public async Task MoveAssetAsync(int assetId, int delta)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var a = await db.InstagramAssets.FirstOrDefaultAsync(x => x.Id == assetId);
        if (a is null) return;
        var list = await db.InstagramAssets.Where(x => x.PostId == a.PostId).OrderBy(x => x.Order).ToListAsync();
        var i = list.FindIndex(x => x.Id == assetId);
        var j = Math.Clamp(i + delta, 0, list.Count - 1);
        if (i == j) return;
        (list[i], list[j]) = (list[j], list[i]);
        for (var k = 0; k < list.Count; k++) list[k].Order = k;
        await db.SaveChangesAsync();
    }

    /// <summary>Plik do publicznego adresu /media/ig/{token}.jpg (tylko zdjęcia).</summary>
    public async Task<byte[]?> AssetBytesAsync(string token)
    {
        if (token.Length != 32 || !token.All(char.IsAsciiHexDigit)) return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.InstagramAssets.AsNoTracking().Where(a => a.Token == token && a.Kind == "image").Select(a => a.Data).FirstOrDefaultAsync();
    }

    public async Task<byte[]?> AssetBytesByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.InstagramAssets.AsNoTracking().Where(a => a.Id == id).Select(a => a.Data).FirstOrDefaultAsync();
    }

    // ── Pomysły → posty ───────────────────────────────────────────────

    /// <summary>Pomysły: z AI (gdy działa), inaczej z wbudowanego banku. Zwraca też, skąd są.</summary>
    public async Task<(List<InstagramAi.Idea> Ideas, string? Note)> IdeasAsync(int count, string? pillar, string format, string? brief, CancellationToken ct = default)
    {
        var recent = await RecentTitlesAsync();
        var unavailable = await ai.UnavailableAsync();
        if (unavailable is not null)
            return (InstagramAi.FromBank(pillar, recent, count), $"AI niedostępne ({unavailable.TrimEnd('.')}) — pomysły z gotowego banku.");
        var ap = await AutopilotAsync();
        var pillars = Enumerable.Range(0, count).Select(i => pillar ?? ap.Pillars[(i + Random.Shared.Next(ap.Pillars.Count)) % ap.Pillars.Count]).ToList();
        var results = await Task.WhenAll(pillars.Select((p, i) => ai.IdeaAsync(p, format == "mix" ? (i % 2 == 0 ? "carousel" : "image") : format, recent, brief, ct)));
        var ideas = results.Where(r => r.Idea is not null).Select(r => r.Idea!).ToList();
        if (ideas.Count == 0)
            return (InstagramAi.FromBank(pillar, recent, count), (results.FirstOrDefault().Error ?? "AI nie odpowiedziało") + " — pomysły z gotowego banku.");
        return (ideas, ideas.Count < count ? $"AI przygotowało {ideas.Count} z {count}." : null);
    }

    private async Task<List<string>> RecentTitlesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.InstagramPosts.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(30).Select(p => p.Title).ToListAsync();
    }

    /// <summary>Motyw kolorów dla filaru — profil wygląda spójnie, a każdy rodzaj treści ma swój kolor.</summary>
    public static string ThemeFor(string pillar) => pillar switch
    {
        "funkcja" or "kulisy" => "grafit",
        "mit" => "jasny",
        "pytanie" => "energia",
        _ => "fiolet"
    };

    /// <summary>Post z pomysłu: grafiki z szablonów, opis z hashtagami. Status według <paramref name="status"/>.</summary>
    public async Task<int> CreateFromIdeaAsync(InstagramAi.Idea idea, InstagramPostStatus status = InstagramPostStatus.Draft, DateTime? scheduledAt = null,
        string source = "ai", string? theme = null)
    {
        var acc = await AccountAsync();
        var ap = await AutopilotAsync();
        var domain = (await settings.GetAsync(K.MainDomain)).Trim();
        var slides = idea.Slides.Take(InstagramRules.MaxCarousel).ToList();
        var kind = slides.Count > 1 ? InstagramPostKind.Carousel : InstagramPostKind.Image;
        var caption = InstagramRules.AddHashtags(idea.Caption, idea.Hashtags.Concat(InstagramRules.ParseTags(ap.Hashtags)));
        var id = await CreatePostAsync(kind, InstagramGraphics.Clean(idea.Title, 200), idea.Pillar, source);
        await SavePostAsync(id, idea.Title, caption, kind, idea.Pillar, true);
        theme ??= ThemeFor(idea.Pillar);
        for (var i = 0; i < slides.Count; i++)
        {
            var s = slides[i];
            await AddGraphicAsync(id, new InstagramGraphics.Spec
            {
                Template = s.Template, Theme = theme, Size = "post",
                Eyebrow = s.Eyebrow ?? (i == 0 ? InstagramRules.PillarLabel(idea.Pillar) : null),
                Headline = s.Headline, Body = s.Body, Points = s.Points,
                Mood = s.Template == "cta" ? "celebrate" : i == 0 ? "hello" : "happy",
                Mascot = i == 0 || i == slides.Count - 1,
                Index = slides.Count > 1 ? i + 1 : null, Count = slides.Count > 1 ? slides.Count : null,
                Handle = acc?.Username, Domain = domain
            });
        }
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramPosts.Where(p => p.Id == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, status).SetProperty(p => p.ScheduledAt, scheduledAt));
        return id;
    }

    public Task<(string? Text, string? Error)> CaptionAiAsync(string action, InstagramPost p) =>
        ai.CaptionAsync(action, p.Title, string.Join(" | ", p.Assets.Select(SpecOf).Where(s => s is not null)
            .Select(s => $"{s!.Headline}. {s.Body} {string.Join(", ", s.Points ?? [])}")), p.Caption);

    // ── Publikacja ────────────────────────────────────────────────────

    /// <summary>Tworzy kontener(y) na Instagramie. Post przechodzi w „Publikuję…” (albo „Błąd”).</summary>
    private async Task StartPublishAsync(InstagramPost p, InstagramAccount a, string token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var post = await db.InstagramPosts.FirstAsync(x => x.Id == p.Id, ct);
        post.Attempts++;
        try
        {
            var assets = p.Assets.OrderBy(x => x.Order).ToList();
            var caption = Social.LinkService.ForInstagram(p.Caption);
            string container;
            switch (p.Kind)
            {
                case InstagramPostKind.Carousel:
                    var children = new List<string>();
                    foreach (var asset in assets)
                        children.Add(await client.CreateContainerAsync(token, a.IgUserId, new() { ["image_url"] = AssetUrl(asset), ["is_carousel_item"] = "true" }, ct));
                    container = await client.CreateContainerAsync(token, a.IgUserId, new() { ["media_type"] = "CAROUSEL", ["children"] = string.Join(',', children), ["caption"] = caption }, ct);
                    break;
                case InstagramPostKind.Reel:
                    container = await client.CreateContainerAsync(token, a.IgUserId, new()
                    {
                        ["media_type"] = "REELS", ["video_url"] = AssetUrl(assets[0]), ["caption"] = caption, ["share_to_feed"] = p.ShareToFeed ? "true" : "false"
                    }, ct);
                    break;
                case InstagramPostKind.Story:
                    container = await client.CreateContainerAsync(token, a.IgUserId, assets[0].Kind == "video"
                        ? new() { ["media_type"] = "STORIES", ["video_url"] = AssetUrl(assets[0]) }
                        : new() { ["media_type"] = "STORIES", ["image_url"] = AssetUrl(assets[0]) }, ct);
                    break;
                default:
                    container = await client.CreateContainerAsync(token, a.IgUserId, new() { ["image_url"] = AssetUrl(assets[0]), ["caption"] = caption }, ct);
                    break;
            }
            post.ContainerId = container;
            post.Status = InstagramPostStatus.Publishing;
            post.Error = null;
        }
        catch (InstagramApiException ex)
        {
            Fail(post, ex.Message);
            if (ex.TokenInvalid) await NoteErrorAsync(a.Id, ex.Message);
        }
        post.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (post.Status == InstagramPostStatus.Failed) await NotifyFailureAsync(post);
    }

    private static void Fail(InstagramPost post, string error)
    {
        post.Status = InstagramPostStatus.Failed;
        post.Error = error.Length > 1000 ? error[..1000] : error;
    }

    /// <summary>Sprawdza kontener; gdy gotowy — publikuje i zapisuje adres posta.</summary>
    private async Task AdvanceAsync(int postId, InstagramAccount a, string token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var post = await db.InstagramPosts.FirstOrDefaultAsync(x => x.Id == postId && x.Status == InstagramPostStatus.Publishing, ct);
        if (post?.ContainerId is null) return;
        try
        {
            // Zdjęcia są gotowe po chwili — kilka szybkich prób, film sprawdzamy w kolejnych przebiegach (co minutę).
            var tries = post.Kind is InstagramPostKind.Reel ? 1 : 5;
            var status = "IN_PROGRESS";
            string? detail = null;
            for (var i = 0; i < tries && status == "IN_PROGRESS"; i++)
            {
                if (i > 0) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                (status, detail) = await client.ContainerStatusAsync(token, post.ContainerId, ct);
            }
            switch (status)
            {
                case "FINISHED":
                    var mediaId = await client.PublishAsync(token, a.IgUserId, post.ContainerId, ct);
                    post.IgMediaId = mediaId;
                    post.Status = InstagramPostStatus.Published;
                    post.PublishedAt = DateTime.UtcNow;
                    try { post.Permalink = await client.PermalinkAsync(token, mediaId, ct); } catch (InstagramApiException) { }
                    break;
                case "PUBLISHED":
                    post.Status = InstagramPostStatus.Published;
                    post.PublishedAt ??= DateTime.UtcNow;
                    break;
                case "ERROR" or "EXPIRED":
                    Fail(post, status == "EXPIRED" ? "Instagram nie zdążył opublikować (kontener wygasł) — spróbuj ponownie." : $"Instagram nie przyjął pliku: {detail ?? "błąd przetwarzania"}.");
                    break;
                default:
                    // Film przetwarza się zwykle kilka minut; po 2 godzinach się poddajemy.
                    if (post.UpdatedAt < DateTime.UtcNow.AddHours(-2)) Fail(post, "Instagram przetwarza film zbyt długo — spróbuj z mniejszym plikiem.");
                    else return;
                    break;
            }
        }
        catch (InstagramApiException ex)
        {
            if (ex.Subcode == 2207027) return; // jeszcze nie gotowe
            Fail(post, ex.Message);
        }
        post.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (post.Status == InstagramPostStatus.Failed) await NotifyFailureAsync(post);
    }

    /// <summary>Przebieg usługi w tle: publikuje zaplanowane i domyka publikowane.</summary>
    public async Task<int> PublishDueAsync(CancellationToken ct)
    {
        var due = await PostsAsync(q => q.Where(p => (p.Status == InstagramPostStatus.Scheduled && p.ScheduledAt <= DateTime.UtcNow) || p.Status == InstagramPostStatus.Publishing));
        // Posty bez Instagrama: przechodzą w „Publikuję…”, a kanały obsługuje SocialService.
        foreach (var p in due.Where(p => !p.ToInstagram && p.Status == InstagramPostStatus.Scheduled))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var problems = await social.ProblemsAsync(p);
            await db.InstagramPosts.Where(x => x.Id == p.Id).ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, problems.Count > 0 ? InstagramPostStatus.Failed : InstagramPostStatus.Publishing)
                .SetProperty(x => x.Error, problems.Count > 0 ? string.Join(" ", problems) : null).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
        }
        var ig = due.Where(p => p.ToInstagram).ToList();
        if (ig.Count > 0)
        {
            var (a, token, error) = await ConnectedAsync();
            if (error is not null)
            {
                // Bez połączenia z Instagramem nie publikujemy tam — posty czekają, a w Przeglądzie widać powód.
                foreach (var p in ig.Where(p => p.Status == InstagramPostStatus.Scheduled && p.ScheduledAt < DateTime.UtcNow.AddHours(-6)))
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    await db.InstagramPosts.Where(x => x.Id == p.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, InstagramPostStatus.Failed).SetProperty(x => x.Error, error), ct);
                }
            }
            else
            {
                foreach (var p in ig)
                {
                    if (p.Status == InstagramPostStatus.Scheduled)
                    {
                        if (Problems(p) is { Count: > 0 } problems)
                        {
                            await using var db = await dbFactory.CreateDbContextAsync(ct);
                            await db.InstagramPosts.Where(x => x.Id == p.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, InstagramPostStatus.Failed).SetProperty(x => x.Error, string.Join(" ", problems)), ct);
                            continue;
                        }
                        await StartPublishAsync(p, a!, token!, ct);
                    }
                    await AdvanceAsync(p.Id, a!, token!, ct);
                }
                await using var db2 = await dbFactory.CreateDbContextAsync(ct);
                var ids = ig.Select(p => p.Id).ToList();
                if (await db2.InstagramPosts.AnyAsync(p => ids.Contains(p.Id) && p.Status == InstagramPostStatus.Published, ct))
                    await SafeAsync(() => SyncAsync(ct), "synchronizacja po publikacji");
            }
        }
        await social.ProcessDueAsync(ct);
        return due.Count;
    }

    private async Task NotifyFailureAsync(InstagramPost post)
    {
        var to = await settings.GetAsync(K.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return;
        var link = $"{PortalUrl}/panel/instagram?t=plan&post={post.Id}";
        await email.SendAsync(to, $"Instagram: nie opublikowano „{post.Title}”",
            $"<p>Post <b>{WebUtility.HtmlEncode(post.Title)}</b> nie trafił na Instagram.</p><p>Powód: {WebUtility.HtmlEncode(post.Error ?? "—")}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Otwórz w Portalu</a></p>");
    }

    // ── Synchronizacja ────────────────────────────────────────────────

    /// <summary>Profil, ostatnie posty i ich statystyki (świeże posty częściej).</summary>
    public async Task<(bool Ok, string Message)> SyncAsync(CancellationToken ct = default)
    {
        var (a, token, error) = await ConnectedAsync();
        if (error is not null) return (false, error);
        try
        {
            var me = a!.Provider == "facebook" ? await client.ProfileAsync(token!, a.IgUserId, ct) : await client.MeAsync(token!, ct);
            var media = await client.MediaAsync(token!, a.IgUserId, 50, ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var acc = await db.InstagramAccounts.FirstAsync(x => x.Id == a.Id, ct);
            acc.Username = me.Username; acc.Name = me.Name; acc.PictureUrl = me.Picture;
            acc.Followers = me.Followers; acc.Follows = me.Follows; acc.MediaCount = me.MediaCount;
            acc.LastSyncAt = DateTime.UtcNow; acc.LastError = null;

            var ids = media.Select(m => m.Id).ToList();
            var existing = await db.InstagramMedia.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
            var posts = await db.InstagramPosts.Where(p => p.IgMediaId != null && ids.Contains(p.IgMediaId)).ToDictionaryAsync(p => p.IgMediaId!, p => p.Id, ct);
            var now = DateTime.UtcNow;
            foreach (var m in media)
            {
                if (!existing.TryGetValue(m.Id, out var row)) db.InstagramMedia.Add(row = new InstagramMedia { Id = m.Id });
                row.MediaType = m.MediaType; row.ProductType = m.ProductType; row.Caption = m.Caption; row.Permalink = m.Permalink;
                row.ThumbnailUrl = m.MediaType == "VIDEO" ? m.ThumbnailUrl ?? m.MediaUrl : m.MediaUrl ?? m.ThumbnailUrl;
                row.PostedAt = m.Timestamp; row.Likes = m.Likes; row.Comments = m.Comments;
                if (posts.TryGetValue(m.Id, out var pid)) row.PostId = pid;
                // Statystyki: posty z ostatnich 7 dni co przebieg, do 60 dni raz na dobę, starsze raz na tydzień.
                var age = now - m.Timestamp;
                var every = age < TimeSpan.FromDays(7) ? TimeSpan.Zero : age < TimeSpan.FromDays(60) ? TimeSpan.FromHours(20) : TimeSpan.FromDays(7);
                if (row.InsightsAt is null || now - row.InsightsAt > every)
                {
                    var ins = await client.MediaInsightsAsync(token!, m.Id, m.ProductType, ct);
                    row.Views = ins.GetValueOrDefault("views", row.Views ?? 0);
                    row.Reach = ins.TryGetValue("reach", out var r) ? r : row.Reach;
                    row.Saved = ins.TryGetValue("saved", out var s) ? s : row.Saved;
                    row.Shares = ins.TryGetValue("shares", out var sh) ? sh : row.Shares;
                    row.Interactions = ins.TryGetValue("total_interactions", out var ti) ? ti : row.Interactions;
                    row.InsightsAt = now;
                }
            }
            await db.SaveChangesAsync(ct);
            await SnapshotAsync(acc, token!, ct);
            return (true, $"Zsynchronizowano: {media.Count} postów, {me.Followers} obserwujących.");
        }
        catch (InstagramApiException ex)
        {
            await NoteErrorAsync(a!.Id, ex.Message);
            return (false, ex.Message);
        }
    }

    /// <summary>Dzisiejszy stan obserwujących i wczorajsze (pełne) statystyki konta.</summary>
    private async Task SnapshotAsync(InstagramAccount acc, string token, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(InstagramRules.ToWarsaw(DateTime.UtcNow));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.InstagramDaily.FirstOrDefaultAsync(d => d.Day == today, ct);
        if (row is null) db.InstagramDaily.Add(row = new InstagramDaily { Day = today });
        row.Followers = acc.Followers;

        var yesterday = today.AddDays(-1);
        var y = await db.InstagramDaily.FirstOrDefaultAsync(d => d.Day == yesterday, ct);
        if (y is null || y.Reach is null)
        {
            var since = InstagramRules.ToUtc(yesterday.ToDateTime(TimeOnly.MinValue));
            var ins = await client.AccountInsightsAsync(token, acc.IgUserId, since, since.AddDays(1), ct);
            if (ins.Count > 0)
            {
                if (y is null) db.InstagramDaily.Add(y = new InstagramDaily { Day = yesterday, Followers = acc.Followers });
                y.Reach = ins.GetValueOrDefault("reach");
                y.Views = ins.TryGetValue("views", out var v) ? v : null;
                y.Interactions = ins.TryGetValue("total_interactions", out var ti) ? ti : null;
                y.Engaged = ins.TryGetValue("accounts_engaged", out var e) ? e : null;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Komentarze pod ostatnimi postami. Nowe trafiają do skrzynki; autopilot może odpowiedzieć na proste pochwały.</summary>
    /// <summary>Komentarze z Instagrama i z pozostałych kanałów (Facebook) — do wspólnej skrzynki.</summary>
    public async Task<(int New, string? Error)> SyncCommentsAsync(CancellationToken ct = default)
    {
        var hasIg = await AccountAsync() is not null;
        var (igNew, igError) = hasIg ? await SyncInstagramCommentsAsync(ct) : (0, null);
        var (otherNew, otherError) = await social.SyncAsync(ct);
        return (igNew + otherNew, igError ?? otherError);
    }

    private async Task<(int New, string? Error)> SyncInstagramCommentsAsync(CancellationToken ct)
    {
        var (a, token, error) = await ConnectedAsync();
        if (error is not null) return (0, error);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var recent = await db.InstagramMedia.AsNoTracking().Where(m => m.Platform == "instagram" && m.ProductType != "STORY").OrderByDescending(m => m.PostedAt).Take(15).Select(m => m.Id).ToListAsync(ct);
        var added = 0;
        try
        {
            foreach (var mediaId in recent)
            {
                var comments = await client.CommentsAsync(token!, mediaId, ct);
                var ids = comments.Select(c => c.Id).ToList();
                var known = await db.InstagramComments.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
                foreach (var c in comments)
                {
                    var own = string.Equals(c.Username, a!.Username, StringComparison.OrdinalIgnoreCase);
                    if (known.TryGetValue(c.Id, out var row)) { row.Hidden = c.Hidden; continue; }
                    db.InstagramComments.Add(new InstagramComment
                    {
                        Id = c.Id, MediaId = mediaId, ParentId = c.ParentId, Username = c.Username, Text = c.Text, At = c.At, Hidden = c.Hidden,
                        IsOwn = own, Done = own
                    });
                    if (!own) added++;
                }
                // Wątek z naszą odpowiedzią = obsłużony.
                var answered = comments.Where(c => c.ParentId is not null && string.Equals(c.Username, a!.Username, StringComparison.OrdinalIgnoreCase)).Select(c => c.ParentId!).ToHashSet();
                foreach (var row in db.ChangeTracker.Entries<InstagramComment>().Select(e => e.Entity).Where(x => answered.Contains(x.Id))) row.Done = true;
                foreach (var row in known.Values.Where(x => answered.Contains(x.Id))) row.Done = true;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (InstagramApiException ex) { return (added, ex.Message); }
        if (added > 0 && (await AutopilotAsync()).CommentMode == "auto") await AutoReplyAsync(ct);
        return (added, null);
    }

    public async Task<List<InstagramComment>> CommentsAsync(bool openOnly)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.InstagramComments.AsNoTracking().Where(c => !c.IsOwn);
        if (openOnly) q = q.Where(c => !c.Done && !c.Hidden);
        return await q.OrderByDescending(c => c.At).Take(200).ToListAsync();
    }

    public async Task<Dictionary<string, InstagramMedia>> MediaByIdAsync(IEnumerable<string> ids)
    {
        var list = ids.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.InstagramMedia.AsNoTracking().Where(m => list.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
    }

    public async Task<(bool Ok, string Message)> ReplyAsync(string commentId, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return (false, "Wpisz odpowiedź.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var c = await db.InstagramComments.FirstOrDefaultAsync(x => x.Id == commentId);
        if (c is null) return (false, "Nie ma takiego komentarza.");
        if (c.Platform != "instagram") return await social.ReplyAsync(c, text);
        var (a, token, error) = await ConnectedAsync();
        if (error is not null) return (false, error);
        try
        {
            // Odpowiedź zawsze do komentarza głównego (Instagram nie ma głębszych wątków).
            var replyId = await client.ReplyAsync(token!, c.ParentId ?? c.Id, text);
            c.Done = true;
            if (replyId is not null)
                db.InstagramComments.Add(new InstagramComment { Id = replyId, MediaId = c.MediaId, ParentId = c.ParentId ?? c.Id, Username = a!.Username, Text = text, At = DateTime.UtcNow, IsOwn = true, Done = true });
            await db.SaveChangesAsync();
            return (true, "Odpowiedź opublikowana.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    public async Task<(bool Ok, string Message)> HideCommentAsync(string commentId, bool hide)
    {
        await using (var cdb = await dbFactory.CreateDbContextAsync())
            if (await cdb.InstagramComments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == commentId) is { Platform: not "instagram" } other)
                return await social.HideAsync(other, hide);
        var (_, token, error) = await ConnectedAsync();
        if (error is not null) return (false, error);
        try
        {
            await client.HideAsync(token!, commentId, hide);
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.InstagramComments.Where(c => c.Id == commentId).ExecuteUpdateAsync(u => u.SetProperty(c => c.Hidden, hide).SetProperty(c => c.Done, true));
            return (true, hide ? "Komentarz ukryty." : "Komentarz znów widoczny.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    public async Task MarkDoneAsync(string commentId, bool done = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramComments.Where(c => c.Id == commentId).ExecuteUpdateAsync(u => u.SetProperty(c => c.Done, done));
    }

    public async Task<(string? Text, string? Error)> SuggestReplyAsync(string commentId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var c = await db.InstagramComments.FirstOrDefaultAsync(x => x.Id == commentId);
        if (c is null) return (null, "Nie ma takiego komentarza.");
        var caption = await db.InstagramMedia.Where(m => m.Id == c.MediaId).Select(m => m.Caption).FirstOrDefaultAsync();
        var r = await ai.ReplyAsync(c.Text, c.Username, caption);
        if (r.Text is not null) { c.Suggestion = r.Text.Length > 1000 ? r.Text[..1000] : r.Text; await db.SaveChangesAsync(); }
        return r;
    }

    /// <summary>Autopilot komentarzy: tylko krótkie pochwały bez pytań (reszta czeka na człowieka).</summary>
    private async Task AutoReplyAsync(CancellationToken ct)
    {
        if (await ai.UnavailableAsync() is not null) return;
        var open = (await CommentsAsync(openOnly: true)).Where(c => c.ParentId is null && InstagramRules.IsSimplePraise(c.Text) && c.At > DateTime.UtcNow.AddDays(-3)).Take(10).ToList();
        foreach (var c in open)
        {
            var (text, _) = await SuggestReplyAsync(c.Id);
            if (!string.IsNullOrWhiteSpace(text)) await ReplyAsync(c.Id, text);
            ct.ThrowIfCancellationRequested();
        }
    }

    // ── Autopilot treści ──────────────────────────────────────────────

    /// <summary>
    /// Uzupełnia plan na 7 dni: na każdy wolny termin z ustawień przygotowuje post (AI albo bank pomysłów).
    /// „drafts” — czeka na akceptację, „auto” — od razu zaplanowany. Zwraca liczbę nowych postów.
    /// </summary>
    public async Task<(int Created, string? Note)> RunAutopilotAsync(bool force = false, CancellationToken ct = default)
    {
        var ap = await AutopilotAsync();
        if (ap.Mode == "off" && !force) return (0, "Autopilot jest wyłączony.");
        var (fromSeries, seriesNote) = await FillSeriesAsync(ap, ct);
        var planned = await PostsAsync(q => q.Where(p => p.ScheduledAt != null && p.ScheduledAt > DateTime.UtcNow
            && (p.Status == InstagramPostStatus.Scheduled || p.Status == InstagramPostStatus.ToApprove || p.Status == InstagramPostStatus.Publishing)));
        var weekEnd = DateTime.UtcNow.AddDays(7);
        var inWeek = planned.Count(p => p.ScheduledAt < weekEnd);
        var need = Math.Max(0, ap.PostsPerWeek - inWeek);
        if (need == 0) return (fromSeries, fromSeries > 0 ? $"Serie: {fromSeries} nowe posty. Plan na tydzień jest pełny." : seriesNote ?? "Plan na najbliższy tydzień jest pełny.");
        var slots = InstagramRules.FreeSlots(DateTime.UtcNow, ap.Days, ap.Time, planned.Select(p => p.ScheduledAt!.Value), need, 7);
        if (slots.Count == 0) return (fromSeries, fromSeries > 0 ? $"Serie: {fromSeries} nowe posty." : "Brak wolnych terminów w wybrane dni w tym tygodniu.");

        // Filary po kolei, z pominięciem ostatnio użytych; oferta najwyżej raz na 5 postów.
        var recent = await PostsAsync(q => q.OrderByDescending(p => p.CreatedAt).Take(5));
        var pillars = ap.Pillars.OrderBy(p => recent.FindIndex(r => r.Pillar == p) is var i && i < 0 ? -1 : 5 - i).ThenBy(_ => Random.Shared.Next()).ToList();
        if (recent.Any(r => r.Pillar == "oferta")) pillars.Remove("oferta");
        if (pillars.Count == 0) pillars = ["porada"];

        var created = 0;
        string? note = null;
        var titles = await RecentTitlesAsync();
        for (var i = 0; i < slots.Count; i++)
        {
            var pillar = pillars[i % pillars.Count];
            InstagramAi.Idea? idea = null;
            if (await ai.UnavailableAsync() is null)
            {
                var (aiIdea, err) = await ai.IdeaAsync(pillar, i % 2 == 0 ? "carousel" : "image", titles, null, ct);
                idea = aiIdea;
                if (err is not null) note = err;
            }
            idea ??= InstagramAi.FromBank(pillar, titles, 1).FirstOrDefault();
            if (idea is null) continue;
            titles.Insert(0, idea.Title);
            await CreateFromIdeaAsync(idea, ap.Mode == "auto" ? InstagramPostStatus.Scheduled : InstagramPostStatus.ToApprove, slots[i], "autopilot");
            created++;
        }
        created += fromSeries;
        if (created > 0 && ap.Mode == "drafts") await NotifyDraftsAsync(created);
        return (created, note ?? seriesNote);
    }

    // ── Serie (stałe cykle) ──────────────────────────────────────────

    public async Task<List<ContentSeries>> SeriesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.ContentSeries.AsNoTracking().ToListAsync()).OrderBy(s => s.Day).ThenBy(s => s.Time).ToList();
    }

    public async Task<(bool Ok, string Message)> SaveSeriesAsync(ContentSeries input)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length is < 2 or > 80) return (false, "Nazwa serii: od 2 do 80 znaków, np. „Wtorkowa porada”.");
        if (input.Day is < 1 or > 7) return (false, "Wybierz dzień tygodnia.");
        if (InstagramRules.FindPillar(input.Pillar) is null) return (false, "Wybierz filar treści.");
        var brief = (input.Brief ?? "").Trim();
        if (brief.Length > 500) return (false, "Temat przewodni: najwyżej 500 znaków.");
        var tag = string.IsNullOrWhiteSpace(input.Hashtag) ? null : "#" + new string(input.Hashtag.Trim().TrimStart('#').Where(char.IsLetterOrDigit).ToArray());
        if (tag is { Length: < 2 }) tag = null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var s = input.Id > 0 ? await db.ContentSeries.FirstOrDefaultAsync(x => x.Id == input.Id) : null;
        if (s is null) db.ContentSeries.Add(s = new ContentSeries());
        (s.Name, s.Day, s.Time, s.Pillar, s.Format, s.Brief, s.Hashtag, s.Enabled) =
            (name, input.Day, input.Time, input.Pillar, input.Format == "image" ? "image" : "carousel", brief, tag, input.Enabled);
        await db.SaveChangesAsync();
        return (true, "Zapisano serię — autopilot przygotuje ją na najbliższy termin.");
    }

    public async Task DeleteSeriesAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramPosts.Where(p => p.SeriesId == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.SeriesId, (int?)null));
        await db.ContentSeries.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Najbliższy termin serii w ciągu 7 dni (czas UTC); null — brak (np. dziś już po godzinie).</summary>
    public static DateTime? NextSeriesSlot(ContentSeries s, TimeOnly defaultTime, DateTime nowUtc)
    {
        var today = InstagramRules.ToWarsaw(nowUtc).Date;
        for (var d = 0; d < 7; d++)
        {
            var day = today.AddDays(d);
            if (InstagramRules.IsoDay(day) != s.Day) continue;
            var at = InstagramRules.ToUtc(day.Add((s.Time ?? defaultTime).ToTimeSpan()));
            if (at > nowUtc.AddMinutes(30)) return at;
        }
        return null;
    }

    /// <summary>Serie: na najbliższy termin każdej włączonej serii — post z pomysłu AI (albo z banku) z tematem przewodnim i hasztagiem serii.</summary>
    private async Task<(int Created, string? Note)> FillSeriesAsync(Autopilot ap, CancellationToken ct)
    {
        var series = (await SeriesAsync()).Where(s => s.Enabled).ToList();
        if (series.Count == 0) return (0, null);
        var created = 0;
        string? note = null;
        var titles = await RecentTitlesAsync();
        foreach (var s in series)
        {
            if (NextSeriesSlot(s, ap.Time, DateTime.UtcNow) is not { } at) continue;
            var dayStart = InstagramRules.ToUtc(InstagramRules.ToWarsaw(at).Date);
            var dayEnd = dayStart.AddDays(1);
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                if (await db.InstagramPosts.AnyAsync(p => p.SeriesId == s.Id && p.ScheduledAt >= dayStart && p.ScheduledAt < dayEnd, ct)) continue;
            InstagramAi.Idea? idea = null;
            if (await ai.UnavailableAsync() is null)
            {
                var (aiIdea, err) = await ai.IdeaAsync(s.Pillar, s.Format, titles, $"Seria „{s.Name}”. {s.Brief}".Trim(), ct);
                idea = aiIdea;
                if (err is not null) note = err;
            }
            idea ??= InstagramAi.FromBank(s.Pillar, titles, 1).FirstOrDefault();
            if (idea is null) continue;
            titles.Insert(0, idea.Title);
            var id = await CreateFromIdeaAsync(idea, ap.Mode == "auto" ? InstagramPostStatus.Scheduled : InstagramPostStatus.ToApprove, at, "autopilot");
            // Hasztag serii dopisujemy do opisu posta (pomysł z banku jest współdzielony — nie zmieniamy go).
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                if (await db.InstagramPosts.FirstOrDefaultAsync(p => p.Id == id, ct) is { } post)
                {
                    post.SeriesId = s.Id;
                    if (s.Hashtag is { } tag && !InstagramRules.Hashtags(post.Caption).Contains(InstagramRules.NormalizeTag(tag), StringComparer.OrdinalIgnoreCase))
                    {
                        var withTag = InstagramRules.AddHashtags(post.Caption, [tag], InstagramRules.MaxHashtags);
                        post.Caption = withTag.Length <= InstagramRules.MaxCaption ? withTag : post.Caption;
                    }
                    await db.SaveChangesAsync(ct);
                }
            created++;
        }
        return (created, note);
    }

    private async Task NotifyDraftsAsync(int created)
    {
        var to = await settings.GetAsync(K.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return;
        var link = $"{PortalUrl}/panel/instagram?t=plan";
        await email.SendAsync(to, $"Instagram: {created} {(created == 1 ? "post czeka" : "posty czekają")} na akceptację",
            $"<p>Autopilot przygotował {created} {(created == 1 ? "post" : "posty")} na najbliższy tydzień. Sprawdź grafiki i opisy, popraw, co trzeba, i zaakceptuj.</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Otwórz plan w Portalu</a></p>");
    }

    // ── Przegląd i statystyki ─────────────────────────────────────────

    public sealed record Overview(
        InstagramAccount? Account, int FollowersDelta7, int? Reach7, int? Reach7Prev, int? Views7, double? Engagement30,
        int ToApprove, int Scheduled, int Failed, int OpenComments, List<InstagramPost> Next, InstagramMedia? Best, List<InstagramRules.Slot> BestSlots,
        List<(string Tone, string Icon, string Text, string? Tab)> Tips, string AutopilotMode, DateTime? LastPublished);

    public async Task<Overview> OverviewAsync()
    {
        var acc = await AccountAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(InstagramRules.ToWarsaw(DateTime.UtcNow));
        var days = await db.InstagramDaily.AsNoTracking().Where(d => d.Day >= today.AddDays(-15)).OrderBy(d => d.Day).ToListAsync();
        int? Sum(IEnumerable<InstagramDaily> rows, Func<InstagramDaily, int?> f) => rows.Any(r => f(r) is not null) ? rows.Sum(r => f(r) ?? 0) : null;
        var last7 = days.Where(d => d.Day > today.AddDays(-8) && d.Day < today).ToList();
        var prev7 = days.Where(d => d.Day > today.AddDays(-15) && d.Day <= today.AddDays(-8)).ToList();
        var weekAgo = days.FirstOrDefault(d => d.Day >= today.AddDays(-7));
        var media30 = await db.InstagramMedia.AsNoTracking().Where(m => m.Platform == "instagram" && m.PostedAt > DateTime.UtcNow.AddDays(-30)).ToListAsync();
        var allMedia = await db.InstagramMedia.AsNoTracking().Where(m => m.Platform == "instagram").OrderByDescending(m => m.PostedAt).Take(100).ToListAsync();
        var followers = acc?.Followers ?? 0;
        var rates = media30.Select(m => InstagramRules.EngagementRate(m, followers)).Where(r => r is not null).Select(r => r!.Value).ToList();
        var best = media30.Where(m => m.ProductType != "STORY").OrderByDescending(InstagramRules.Interactions).FirstOrDefault();
        var counts = await db.InstagramPosts.GroupBy(p => p.Status).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var next = await db.InstagramPosts.AsNoTracking()
            .Where(p => (p.Status == InstagramPostStatus.Scheduled || p.Status == InstagramPostStatus.ToApprove) && p.ScheduledAt != null)
            .OrderBy(p => p.ScheduledAt).Take(4).ToListAsync();
        await AttachAssetsAsync(db, next);
        var open = await db.InstagramComments.CountAsync(c => !c.Done && !c.Hidden && !c.IsOwn);
        var lastPublished = allMedia.Where(m => m.ProductType != "STORY").Select(m => (DateTime?)m.PostedAt).FirstOrDefault();
        var ap = await AutopilotAsync();

        // Wskazówki — proste reguły, bez AI.
        var tips = new List<(string, string, string, string?)>();
        if (acc is not null)
        {
            var left = acc.TokenExpiresAt - DateTime.UtcNow;
            if (left < TimeSpan.FromDays(7)) tips.Add(("danger", "bi-key", $"Dostęp do Instagrama wygasa za {Math.Max(0, (int)left.TotalDays)} dni — połącz konto ponownie.", "polaczenie"));
            if (acc.LastError is { Length: > 0 } err) tips.Add(("danger", "bi-exclamation-triangle", err, "polaczenie"));
            if (lastPublished is { } lp && DateTime.UtcNow - lp > TimeSpan.FromDays(5) && next.Count == 0)
                tips.Add(("warn", "bi-calendar-x", $"Ostatni post {(int)(DateTime.UtcNow - lp).TotalDays} dni temu, a w planie pusto. Profil rośnie dzięki regularności.", "pomysly"));
        }
        if (counts.GetValueOrDefault(InstagramPostStatus.ToApprove) > 0)
            tips.Add(("warn", "bi-hourglass-split", $"{counts[InstagramPostStatus.ToApprove]} od autopilota czeka na akceptację.", "plan"));
        if (counts.GetValueOrDefault(InstagramPostStatus.Failed) > 0)
            tips.Add(("danger", "bi-x-octagon", $"{counts[InstagramPostStatus.Failed]} nie udało się opublikować — sprawdź powód w planie.", "plan"));
        var manualDue = await social.ManualDueCountAsync();
        if (manualDue > 0) tips.Add(("warn", "bi-box-arrow-up", $"{manualDue} {(manualDue == 1 ? "post czeka" : "posty czekają")} na wrzucenie do kanału ręcznego (TikTok, YouTube, LinkedIn…) — paczka jest gotowa.", "plan"));
        var failedTargets = await social.FailedCountAsync();
        if (failedTargets > 0) tips.Add(("danger", "bi-x-octagon", $"Nie udało się opublikować na {failedTargets} kanałach — sprawdź w planie.", "plan"));
        if (open > 0) tips.Add(("info", "bi-chat-dots", $"{open} {(open == 1 ? "komentarz czeka" : "komentarzy czeka")} na odpowiedź. Szybka odpowiedź podbija zasięg posta.", "komentarze"));
        var slots = InstagramRules.BestSlots(allMedia, followers, minPosts: 2);
        if (slots.Count > 0)
            tips.Add(("ok", "bi-clock-history", $"Najlepiej działają posty w {InstagramRules.DayName(slots[0].Day)} około {slots[0].Hour}:00 (śr. zaangażowanie {slots[0].Score}%).", "statystyki"));
        if (ap.Mode == "off" && acc is not null)
            tips.Add(("info", "bi-robot", "Włącz autopilota — przygotuje posty na cały tydzień, a Ty tylko je zaakceptujesz.", "autopilot"));

        return new(acc, weekAgo is null || acc is null ? 0 : acc.Followers - weekAgo.Followers, Sum(last7, d => d.Reach), Sum(prev7, d => d.Reach), Sum(last7, d => d.Views),
            rates.Count > 0 ? Math.Round(rates.Average(), 1) : null,
            counts.GetValueOrDefault(InstagramPostStatus.ToApprove), counts.GetValueOrDefault(InstagramPostStatus.Scheduled), counts.GetValueOrDefault(InstagramPostStatus.Failed),
            open, next, best, slots, tips, ap.Mode, lastPublished);
    }

    public sealed record Stats(List<InstagramDaily> Days, List<InstagramMedia> Media, Dictionary<int, string> PillarByPost, List<InstagramRules.Slot> Slots, int Followers);

    public async Task<Stats> StatsAsync(int days, string platform = "instagram")
    {
        var acc = await AccountAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var from = DateOnly.FromDateTime(InstagramRules.ToWarsaw(DateTime.UtcNow)).AddDays(-days);
        // Dzienne liczby (zasięg, obserwujący) zbieramy dla Instagrama; dla innych kanałów — same posty.
        var daily = platform == "instagram" ? await db.InstagramDaily.AsNoTracking().Where(d => d.Day >= from).OrderBy(d => d.Day).ToListAsync() : [];
        var media = await db.InstagramMedia.AsNoTracking().Where(m => m.Platform == platform).OrderByDescending(m => m.PostedAt).Take(100).ToListAsync();
        var postIds = media.Where(m => m.PostId != null).Select(m => m.PostId!.Value).ToList();
        var pillars = await db.InstagramPosts.AsNoTracking().Where(p => postIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Pillar);
        var followers = platform switch
        {
            "facebook" => await db.SocialChannels.Where(c => c.Platform == SocialPlatform.Facebook).Select(c => c.Followers).FirstOrDefaultAsync() ?? 0,
            "threads" => await db.SocialChannels.Where(c => c.Platform == SocialPlatform.Threads).Select(c => c.Followers).FirstOrDefaultAsync() ?? 0,
            _ => acc?.Followers ?? 0
        };
        return new(daily, media, pillars, InstagramRules.BestSlots(media, followers, 1, 5), followers);
    }

    /// <summary>Podsumowanie liczb dla AI (bez nazw użytkowników).</summary>
    public static string StatsSummary(Stats s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Obserwujący: {s.Followers}.");
        if (s.Days.Count > 1) sb.AppendLine($"Zmiana obserwujących w okresie: {s.Days[^1].Followers - s.Days[0].Followers:+#;-#;0}. Zasięg łącznie: {s.Days.Sum(d => d.Reach ?? 0)}, wyświetlenia: {s.Days.Sum(d => d.Views ?? 0)}.");
        sb.AppendLine("Posty (data, rodzaj, filar, zasięg, interakcje, zapisania, udostępnienia, zaangażowanie %, początek opisu):");
        foreach (var m in s.Media.Where(m => m.ProductType != "STORY").Take(30))
        {
            var pillar = m.PostId is int id && s.PillarByPost.TryGetValue(id, out var p) ? InstagramRules.PillarLabel(p) : "—";
            sb.AppendLine($"- {InstagramRules.ToWarsaw(m.PostedAt):ddd dd.MM HH:mm}, {m.ProductType ?? m.MediaType}, {pillar}, {m.Reach?.ToString() ?? "?"}, {InstagramRules.Interactions(m)}, {m.Saved?.ToString() ?? "?"}, {m.Shares?.ToString() ?? "?"}, {InstagramRules.EngagementRate(m, s.Followers)?.ToString("0.0") ?? "?"}, „{InstagramGraphics.Clean(m.Caption, 70)}”");
        }
        return sb.ToString();
    }

    public async Task<(string? Text, string? Error)> AnalyzeAsync(int days) => await ai.AnalysisAsync(StatsSummary(await StatsAsync(days)));

    private async Task SafeAsync(Func<Task> work, string what)
    {
        try { await work(); }
        catch (Exception ex) { logger.LogWarning(ex, "Instagram: {What} nie powiodła się.", what); }
    }
}

/// <summary>
/// W tle: co minutę publikacja zaplanowanych, co 15 min komentarze, co 3 h statystyki,
/// raz dziennie (od 7:00) odnowienie tokenu i autopilot.
/// </summary>
public class InstagramBackgroundService(IServiceScopeFactory scopes, ILogger<InstagramBackgroundService> logger) : BackgroundService
{
    private DateTime _comments = DateTime.MinValue, _sync = DateTime.MinValue, _dm = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var ig = scope.ServiceProvider.GetRequiredService<InstagramService>();
                var social = scope.ServiceProvider.GetRequiredService<PTScheduler.Portal.Services.Social.SocialService>();
                var hasIg = await ig.AccountAsync() is not null;
                var hasOther = (await social.ChannelsAsync(enabledOnly: true)).Count > 0;
                if (hasIg || hasOther)
                {
                    await ig.PublishDueAsync(ct);
                    var now = DateTime.UtcNow;
                    if (hasIg && now - _sync > TimeSpan.FromHours(3)) { _sync = now; await ig.SyncAsync(ct); }
                    var dm = scope.ServiceProvider.GetRequiredService<PTScheduler.Portal.Services.Social.DmService>();
                    if (now - _comments > TimeSpan.FromMinutes(15)) { _comments = now; await ig.SyncCommentsAsync(ct); await dm.ApplyRulesAsync(ct); }
                    if (now - _dm > TimeSpan.FromMinutes(5)) { _dm = now; await dm.SyncAsync(ct); }
                    await DailyAsync(scope.ServiceProvider, ig, ct);
                    await scope.ServiceProvider.GetRequiredService<PTScheduler.Portal.Services.Social.SocialReportService>().SendIfDueAsync(ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Instagram: przebieg w tle nie powiódł się."); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static async Task DailyAsync(IServiceProvider sp, InstagramService ig, CancellationToken ct)
    {
        var local = InstagramRules.ToWarsaw(DateTime.UtcNow);
        if (local.Hour < 7) return;
        var settings = sp.GetRequiredService<SiteSettingsService>();
        var today = local.ToString("yyyy-MM-dd");
        if (await settings.GetAsync(K.InstagramAutopilotRunAt) == today) return;
        await settings.SetAsync(K.InstagramAutopilotRunAt, today);
        await ig.RefreshTokenIfNeededAsync(ct);
        await sp.GetRequiredService<PTScheduler.Portal.Services.Social.SocialService>().RefreshTokensAsync(ct);
        await ig.RunAutopilotAsync(ct: ct);
        await sp.GetRequiredService<PTScheduler.Portal.Services.Social.DmService>().CleanupAsync(ct);
    }
}
