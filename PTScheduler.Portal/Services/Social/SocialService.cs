using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Media społecznościowe poza Instagramem: kanały (Facebook i Threads przez API, TikTok, YouTube, LinkedIn, X, Pinterest — ręcznie),
/// połączenie z Meta przez stronę na Facebooku (także Instagram przez stronę), cele postów, publikacja w terminie,
/// przypomnienia o kanałach ręcznych, komentarze i statystyki z Facebooka i Threads.
/// Post i jego termin żyją w module Instagrama (<see cref="InstagramPost"/>) — tu są tylko jego dodatkowe kanały.
/// </summary>
public class SocialService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    FacebookClient facebook,
    ThreadsClient threads,
    InstagramAi ai,
    EmailService email,
    IDataProtectionProvider dataProtection,
    IConfiguration config,
    ILogger<SocialService> logger)
{
    /// <summary>Ten sam cel szyfrowania co token Instagrama — konto Instagram przez stronę korzysta z tokenu strony.</summary>
    private IDataProtector Secrets => dataProtection.CreateProtector("instagram");
    private string PortalUrl => PortalUrls.Public(config);

    private string? Unprotect(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); } catch (CryptographicException) { return null; }
    }

    public string AssetUrl(InstagramAsset a) => a.Kind == "video" ? a.ExternalUrl ?? "" : $"{PortalUrl}/media/ig/{a.Token}.jpg";

    // ── Kanały ────────────────────────────────────────────────────────

    public async Task<List<SocialChannel>> ChannelsAsync(bool enabledOnly = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.SocialChannels.AsNoTracking();
        if (enabledOnly) q = q.Where(c => c.Enabled);
        return (await q.ToListAsync()).OrderBy(c => c.Platform).ThenBy(c => c.Name).ToList();
    }

    public async Task SetFlagsAsync(int id, bool enabled, bool isDefault)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SocialChannels.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Enabled, enabled).SetProperty(c => c.Default, isDefault));
    }

    public async Task DeleteChannelAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SocialChannels.Where(c => c.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Kanał ręczny: Portal przypomni o terminie i przygotuje opis oraz pliki do wrzucenia.</summary>
    public async Task<(bool Ok, string Message)> AddManualAsync(SocialPlatform platform, string name, string? profileUrl)
    {
        if (SocialPlatforms.Get(platform).Api) return (false, "Ten kanał łączy się przez API — użyj przycisku połączenia.");
        await using var db = await dbFactory.CreateDbContextAsync();
        db.SocialChannels.Add(new SocialChannel
        {
            Platform = platform, Api = false, Name = string.IsNullOrWhiteSpace(name) ? SocialPlatforms.Get(platform).Label : name.Trim(),
            ProfileUrl = string.IsNullOrWhiteSpace(profileUrl) ? null : profileUrl.Trim(), Default = false
        });
        await db.SaveChangesAsync();
        return (true, $"Dodano kanał {SocialPlatforms.Get(platform).Label}.");
    }

    // ── Meta: strona na Facebooku (i Instagram przez stronę) ──────────

    public async Task<(string AppId, bool HasSecret)> MetaAppAsync()
    {
        var s = await settings.GetAllAsync(K.MetaAppId, K.MetaAppSecret);
        return (s[K.MetaAppId].Trim(), !string.IsNullOrWhiteSpace(s[K.MetaAppSecret]));
    }

    public async Task SaveMetaAppAsync(string appId, string? secret)
    {
        var values = new Dictionary<string, string> { [K.MetaAppId] = appId.Trim() };
        if (!string.IsNullOrWhiteSpace(secret)) values[K.MetaAppSecret] = Secrets.Protect(secret.Trim());
        await settings.SetManyAsync(values);
    }

    /// <summary>
    /// Token użytkownika (np. z Graph API Explorer, zaczyna się od EAA) → długi token (jeśli znamy klucz aplikacji) → lista stron.
    /// Tokeny stron zostają w pamięci serwera, nie trafiają do przeglądarki.
    /// </summary>
    public async Task<(List<FacebookClient.Page> Pages, string? Error)> MetaPagesAsync(string userToken)
    {
        userToken = userToken.Trim();
        if (userToken.Length < 20) return ([], "To nie wygląda na token Meta.");
        try
        {
            var (appId, hasSecret) = await MetaAppAsync();
            if (appId.Length > 0 && hasSecret && Unprotect(await settings.GetAsync(K.MetaAppSecret)) is { } secret)
            {
                try { userToken = await facebook.LongLivedUserTokenAsync(appId, secret, userToken); }
                catch (InstagramApiException ex) when (!ex.TokenInvalid) { }
            }
            var pages = await facebook.PagesAsync(userToken);
            return pages.Count == 0 ? ([], "Token nie widzi żadnej strony. Zaznacz przy generowaniu uprawnienia do stron (pages_show_list, pages_manage_posts…) i wybierz stronę Hantly.") : (pages, null);
        }
        catch (InstagramApiException ex) { return ([], ex.Message); }
    }

    /// <summary>Zapisuje stronę jako kanał Facebook; opcjonalnie podłącza też Instagram przypięty do tej strony.</summary>
    public async Task<(bool Ok, string Message)> ConnectPageAsync(FacebookClient.Page page, bool alsoInstagram)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ch = await db.SocialChannels.FirstOrDefaultAsync(c => c.Platform == SocialPlatform.Facebook && c.ExternalId == page.Id);
        if (ch is null) db.SocialChannels.Add(ch = new SocialChannel { Platform = SocialPlatform.Facebook, Api = true, Default = true });
        ch.ExternalId = page.Id;
        ch.Name = page.Name;
        ch.PictureUrl = page.Picture;
        ch.ProfileUrl = page.Link;
        ch.Followers = page.Followers;
        ch.Token = Secrets.Protect(page.Token);
        ch.TokenExpiresAt = null;
        ch.TokenRefreshedAt = DateTime.UtcNow;
        ch.ConnectedAt = DateTime.UtcNow;
        ch.LastError = null;
        ch.Enabled = true;
        var msg = $"Połączono stronę „{page.Name}”.";

        if (alsoInstagram && page.IgId is { Length: > 0 } igId)
        {
            var a = await db.InstagramAccounts.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (a is null) db.InstagramAccounts.Add(a = new InstagramAccount());
            else if (a.IgUserId != igId)
            {
                await db.InstagramMedia.Where(m => m.Platform == "instagram").ExecuteDeleteAsync();
                await db.InstagramComments.Where(c => c.Platform == "instagram").ExecuteDeleteAsync();
                await db.InstagramDaily.ExecuteDeleteAsync();
            }
            a.Provider = "facebook";
            a.IgUserId = igId;
            a.Username = page.IgUsername ?? a.Username;
            a.PictureUrl = page.IgPicture ?? a.PictureUrl;
            a.Token = Secrets.Protect(page.Token);
            a.TokenExpiresAt = DateTime.UtcNow.AddYears(10); // token strony z długiego tokenu użytkownika nie ma terminu
            a.TokenRefreshedAt = DateTime.UtcNow;
            a.ConnectedAt = DateTime.UtcNow;
            a.LastError = null;
            msg += $" Instagram @{page.IgUsername} też połączony (przez stronę).";
        }
        else if (alsoInstagram) msg += " Ta strona nie ma podpiętego konta Instagram (Ustawienia strony → Połączone konta).";
        await db.SaveChangesAsync();
        return (true, msg);
    }

    // ── Threads ──────────────────────────────────────────────────────

    public async Task<(bool Ok, string Message)> ConnectThreadsAsync(string token)
    {
        token = token.Trim();
        if (token.Length < 20) return (false, "To nie wygląda na token Threads.");
        try
        {
            var (id, username, picture) = await threads.MeAsync(token);
            await using var db = await dbFactory.CreateDbContextAsync();
            var ch = await db.SocialChannels.FirstOrDefaultAsync(c => c.Platform == SocialPlatform.Threads && c.ExternalId == id);
            if (ch is null) db.SocialChannels.Add(ch = new SocialChannel { Platform = SocialPlatform.Threads, Api = true, Default = true });
            ch.ExternalId = id; ch.Name = "@" + username; ch.Handle = username; ch.PictureUrl = picture;
            ch.ProfileUrl = $"https://www.threads.com/@{username}";
            ch.Token = Secrets.Protect(token);
            ch.TokenExpiresAt = DateTime.UtcNow.AddDays(59);
            ch.TokenRefreshedAt = DateTime.UtcNow.AddDays(-2); // pozwól odnowić przy najbliższym przebiegu — poznamy prawdziwy termin
            ch.ConnectedAt = DateTime.UtcNow; ch.LastError = null; ch.Enabled = true;
            await db.SaveChangesAsync();
            return (true, $"Połączono Threads @{username}.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    /// <summary>Tokeny Threads żyją 60 dni — odnawiamy, gdy zostało mniej niż 20.</summary>
    public async Task RefreshTokensAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var ch in await db.SocialChannels.Where(c => c.Platform == SocialPlatform.Threads && c.Api && c.Token != null).ToListAsync(ct))
        {
            if (ch.TokenExpiresAt is not { } exp || !InstagramRules.TokenNeedsRefresh(exp, ch.TokenRefreshedAt ?? ch.ConnectedAt, DateTime.UtcNow)) continue;
            if (Unprotect(ch.Token) is not { } token) continue;
            try
            {
                var (fresh, expires) = await threads.RefreshAsync(token, ct);
                ch.Token = Secrets.Protect(fresh); ch.TokenExpiresAt = expires; ch.TokenRefreshedAt = DateTime.UtcNow; ch.LastError = null;
            }
            catch (InstagramApiException ex) { ch.LastError = ex.Message; }
        }
        await db.SaveChangesAsync(ct);
    }

    // ── Cele postów ──────────────────────────────────────────────────

    public async Task<List<SocialPostTarget>> TargetsAsync(int postId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SocialPostTargets.AsNoTracking().Include(t => t.Channel).Where(t => t.PostId == postId).OrderBy(t => t.ChannelId).ToListAsync();
    }

    public async Task<Dictionary<int, List<SocialPostTarget>>> TargetsForAsync(IEnumerable<int> postIds)
    {
        var ids = postIds.ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.SocialPostTargets.AsNoTracking().Include(t => t.Channel).Where(t => ids.Contains(t.PostId)).ToListAsync())
            .GroupBy(t => t.PostId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Nowy post dostaje domyślne kanały (zaznaczone „domyślnie” w Kanałach).</summary>
    public async Task ApplyDefaultsAsync(int postId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var defaults = await db.SocialChannels.Where(c => c.Enabled && c.Default).Select(c => c.Id).ToListAsync();
        foreach (var id in defaults) db.SocialPostTargets.Add(new SocialPostTarget { PostId = postId, ChannelId = id });
        await db.SaveChangesAsync();
    }

    /// <summary>Gdzie publikować: Instagram (tak/nie) + lista kanałów. Opublikowanych celów nie usuwa.</summary>
    public async Task SetTargetsAsync(int postId, bool toInstagram, IReadOnlyCollection<int> channelIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InstagramPosts.Where(p => p.Id == postId).ExecuteUpdateAsync(u => u.SetProperty(p => p.ToInstagram, toInstagram));
        var current = await db.SocialPostTargets.Where(t => t.PostId == postId).ToListAsync();
        foreach (var t in current.Where(t => !channelIds.Contains(t.ChannelId) && t.Status != SocialTargetStatus.Published)) db.SocialPostTargets.Remove(t);
        foreach (var id in channelIds.Where(id => current.All(t => t.ChannelId != id))) db.SocialPostTargets.Add(new SocialPostTarget { PostId = postId, ChannelId = id });
        await db.SaveChangesAsync();
    }

    public async Task SetCaptionAsync(int targetId, string? caption)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SocialPostTargets.Where(t => t.Id == targetId).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.Caption, string.IsNullOrWhiteSpace(caption) ? null : caption).SetProperty(t => t.UpdatedAt, DateTime.UtcNow));
    }

    public async Task RetryTargetAsync(int targetId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SocialPostTargets.Where(t => t.Id == targetId && t.Status == SocialTargetStatus.Failed).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.Status, SocialTargetStatus.Pending).SetProperty(t => t.Error, (string?)null).SetProperty(t => t.Attempts, 0));
    }

    /// <summary>Kanał ręczny: wrzucone — zapisujemy (opcjonalnie) adres posta.</summary>
    public async Task MarkManualDoneAsync(int targetId, string? permalink)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SocialPostTargets.Where(t => t.Id == targetId).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.Status, SocialTargetStatus.Published).SetProperty(t => t.PublishedAt, DateTime.UtcNow)
            .SetProperty(t => t.Permalink, string.IsNullOrWhiteSpace(permalink) ? null : permalink.Trim()).SetProperty(t => t.Error, (string?)null));
    }

    public static string CaptionFor(SocialPostTarget t, InstagramPost p) => string.IsNullOrWhiteSpace(t.Caption) ? p.Caption : t.Caption!;

    /// <summary>Problemy wszystkich wybranych kanałów (bez Instagrama — ten sprawdza moduł Instagrama).</summary>
    public async Task<List<string>> ProblemsAsync(InstagramPost p)
    {
        var targets = await TargetsAsync(p.Id);
        var list = targets.Where(t => t.Channel is { Enabled: true } && t.Status is SocialTargetStatus.Pending or SocialTargetStatus.Failed)
            .SelectMany(t => SocialPlatforms.Problems(t.Channel!.Platform, p, CaptionFor(t, p))).ToList();
        if (!p.ToInstagram && targets.Count == 0) list.Add("Wybierz, gdzie publikować.");
        return list;
    }

    public Task<(string? Text, string? Error)> AdaptCaptionAsync(SocialPlatform platform, InstagramPost p)
    {
        var info = SocialPlatforms.Get(platform);
        return ai.AdaptAsync(info.Label, info.Tone, info.MaxText, p.Title, p.Caption);
    }

    // ── Publikacja ───────────────────────────────────────────────────

    /// <summary>Przebieg w tle: cele postów, których termin minął. <paramref name="onlyPostId"/> — tylko ten post (Opublikuj teraz).</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct, int? onlyPostId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var q = db.SocialPostTargets.Include(t => t.Channel).Include(t => t.Post)
            .Where(t => t.Status == SocialTargetStatus.Pending || t.Status == SocialTargetStatus.Publishing)
            .Where(t => t.Post!.Status == InstagramPostStatus.Publishing || t.Post.Status == InstagramPostStatus.Published || t.Post.Status == InstagramPostStatus.Failed
                        || (t.Post.Status == InstagramPostStatus.Scheduled && t.Post.ScheduledAt <= now));
        if (onlyPostId is int only) q = q.Where(t => t.PostId == only);
        var targets = await q.ToListAsync(ct);
        if (targets.Count == 0 && onlyPostId is null) { await FinalizeAsync(db, ct); return 0; }

        var postIds = targets.Select(t => t.PostId).Distinct().ToList();
        var assets = await db.InstagramAssets.AsNoTracking().Where(a => postIds.Contains(a.PostId))
            .Select(a => new InstagramAsset { Id = a.Id, PostId = a.PostId, Order = a.Order, Kind = a.Kind, Token = a.Token, ExternalUrl = a.ExternalUrl })
            .ToListAsync(ct);
        var manualDue = new List<SocialPostTarget>();
        foreach (var t in targets)
        {
            var post = t.Post!;
            post.Assets = assets.Where(a => a.PostId == post.Id).OrderBy(a => a.Order).ToList();
            var ch = t.Channel!;
            if (!ch.Enabled) { t.Status = SocialTargetStatus.Skipped; continue; }
            if (!ch.Api)
            {
                t.Status = SocialTargetStatus.ManualDue;
                manualDue.Add(t);
                continue;
            }
            var token = Unprotect(ch.Token);
            if (token is null || ch.ExternalId is null) { Fail(t, "Kanał nie jest połączony — połącz go ponownie w Kanałach."); continue; }
            var caption = CaptionFor(t, post);
            if (SocialPlatforms.Problems(ch.Platform, post, caption) is { Count: > 0 } problems) { Fail(t, string.Join(" ", problems)); continue; }
            var images = post.Assets.Where(a => a.Kind == "image").Select(AssetUrl).ToList();
            var video = post.Assets.FirstOrDefault(a => a.Kind == "video") is { } v ? AssetUrl(v) : null;
            try
            {
                t.Attempts++;
                if (ch.Platform == SocialPlatform.Facebook)
                {
                    var id = await facebook.PublishAsync(ch.ExternalId, token, caption, images, video, ct);
                    t.ExternalId = id;
                    t.Status = SocialTargetStatus.Published;
                    t.PublishedAt = DateTime.UtcNow;
                    try { t.Permalink = await facebook.PermalinkAsync(id, token, ct); } catch (InstagramApiException) { }
                }
                else if (ch.Platform == SocialPlatform.Threads)
                {
                    if (t.ContainerId is null)
                    {
                        t.ContainerId = await threads.CreateAsync(ch.ExternalId, token, caption, images, video, ct);
                        t.Status = SocialTargetStatus.Publishing;
                    }
                    var (status, error) = await threads.StatusAsync(t.ContainerId, token, ct);
                    if (status is "FINISHED")
                    {
                        var id = await threads.PublishAsync(ch.ExternalId, token, t.ContainerId, ct);
                        t.ExternalId = id;
                        t.Status = SocialTargetStatus.Published;
                        t.PublishedAt = DateTime.UtcNow;
                        try { t.Permalink = await threads.PermalinkAsync(id, token, ct); } catch (InstagramApiException) { }
                    }
                    else if (status is "ERROR" or "EXPIRED") { t.ContainerId = null; Fail(t, $"Threads nie przyjęło posta: {error ?? status}."); }
                    else if (t.UpdatedAt < DateTime.UtcNow.AddHours(-2)) Fail(t, "Threads przetwarza post zbyt długo.");
                }
            }
            catch (InstagramApiException ex)
            {
                if (ex.TokenInvalid) ch.LastError = ex.Message;
                if (t.Attempts >= 3 || ex.TokenInvalid) Fail(t, ex.Message);
                else t.Error = ex.Message;
            }
            t.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await FinalizeAsync(db, ct);
        await NotifyManualAsync(manualDue, ct);
        await NotifyFailuresAsync(targets.Where(t => t.Status == SocialTargetStatus.Failed).ToList(), ct);
        return targets.Count;
    }

    private static void Fail(SocialPostTarget t, string error)
    {
        t.Status = SocialTargetStatus.Failed;
        t.Error = error.Length > 1000 ? error[..1000] : error;
    }

    /// <summary>Posty bez Instagrama: stan posta wynika z jego kanałów.</summary>
    private static async Task FinalizeAsync(PortalDbContext db, CancellationToken ct)
    {
        var open = await db.InstagramPosts.Where(p => !p.ToInstagram && p.Status == InstagramPostStatus.Publishing).ToListAsync(ct);
        if (open.Count == 0) return;
        var ids = open.Select(p => p.Id).ToList();
        var targets = await db.SocialPostTargets.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(ct);
        foreach (var p in open)
        {
            var mine = targets.Where(t => t.PostId == p.Id).ToList();
            if (mine.Count == 0 || mine.Any(t => t.Status is SocialTargetStatus.Pending or SocialTargetStatus.Publishing)) continue;
            if (mine.Any(t => t.Status == SocialTargetStatus.Failed))
            {
                p.Status = InstagramPostStatus.Failed;
                p.Error = string.Join(" ", mine.Where(t => t.Status == SocialTargetStatus.Failed).Select(t => t.Error).Distinct());
            }
            else
            {
                p.Status = InstagramPostStatus.Published;
                p.PublishedAt ??= DateTime.UtcNow;
                p.Permalink ??= mine.FirstOrDefault(t => t.Permalink is not null)?.Permalink;
            }
            p.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyManualAsync(List<SocialPostTarget> due, CancellationToken ct)
    {
        if (due.Count == 0) return;
        var to = await settings.GetAsync(K.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return;
        var rows = string.Join("", due.Select(t =>
            $"<li><b>{WebUtility.HtmlEncode(SocialPlatforms.Get(t.Channel!.Platform).Label)}</b> — {WebUtility.HtmlEncode(t.Post!.Title)} · <a href=\"{WebUtility.HtmlEncode($"{PortalUrl}/panel/instagram?t=plan&post={t.PostId}")}\">otwórz paczkę</a></li>"));
        await email.SendAsync(to, due.Count == 1 ? $"Czas wrzucić post na {SocialPlatforms.Get(due[0].Channel!.Platform).Label}" : $"Czas wrzucić {due.Count} posty na kanały ręczne",
            $"<p>Nadszedł termin publikacji. Opis i pliki są gotowe w Portalu — skopiuj, wrzuć i kliknij „Opublikowane”.</p><ul>{rows}</ul>");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = due.Select(t => t.Id).ToList();
        await db.SocialPostTargets.Where(t => ids.Contains(t.Id)).ExecuteUpdateAsync(u => u.SetProperty(t => t.NotifiedAt, DateTime.UtcNow), ct);
    }

    private async Task NotifyFailuresAsync(List<SocialPostTarget> failed, CancellationToken ct)
    {
        if (failed.Count == 0) return;
        var to = await settings.GetAsync(K.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return;
        var rows = string.Join("", failed.Select(t => $"<li><b>{WebUtility.HtmlEncode(SocialPlatforms.Get(t.Channel!.Platform).Label)}</b> — {WebUtility.HtmlEncode(t.Post!.Title)}: {WebUtility.HtmlEncode(t.Error ?? "")}</li>"));
        await email.SendAsync(to, "Media społecznościowe: nie opublikowano", $"<p>Nie udało się opublikować:</p><ul>{rows}</ul><p><a href=\"{WebUtility.HtmlEncode(PortalUrl + "/panel/instagram?t=plan")}\">Otwórz plan</a></p>");
    }

    public async Task<int> ManualDueCountAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SocialPostTargets.CountAsync(t => t.Status == SocialTargetStatus.ManualDue);
    }

    public async Task<int> FailedCountAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SocialPostTargets.CountAsync(t => t.Status == SocialTargetStatus.Failed);
    }

    // ── Komentarze i statystyki (Facebook, Threads) ──────────────────

    /// <summary>Ostatnie posty strony z reakcjami i komentarze pod nimi (do wspólnej skrzynki). Zwraca liczbę nowych komentarzy.</summary>
    public async Task<(int New, string? Error)> SyncAsync(CancellationToken ct = default)
    {
        var added = 0;
        string? error = null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        foreach (var ch in await db.SocialChannels.Where(c => c.Api && c.Enabled && c.Token != null).ToListAsync(ct))
        {
            var token = Unprotect(ch.Token);
            if (token is null || ch.ExternalId is null) continue;
            try
            {
                if (ch.Platform == SocialPlatform.Facebook)
                {
                    var info = await facebook.PageInfoAsync(ch.ExternalId, token, ct);
                    ch.Followers = info.Followers; ch.PictureUrl = info.Picture ?? ch.PictureUrl; ch.Name = info.Name.Length > 0 ? info.Name : ch.Name;
                    var posts = await facebook.PostsAsync(ch.ExternalId, token, 15, ct);
                    var ids = posts.Select(p => p.Id).ToList();
                    var known = await db.InstagramMedia.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
                    var linked = await db.SocialPostTargets.Where(t => t.ExternalId != null && ids.Contains(t.ExternalId)).ToDictionaryAsync(t => t.ExternalId!, t => t.PostId, ct);
                    foreach (var p in posts)
                    {
                        if (!known.TryGetValue(p.Id, out var m)) db.InstagramMedia.Add(m = new InstagramMedia { Id = p.Id, Platform = "facebook" });
                        m.MediaType = "FB_POST"; m.Caption = p.Message; m.Permalink = p.Permalink; m.ThumbnailUrl = p.Picture; m.PostedAt = p.CreatedAt;
                        m.Likes = p.Reactions; m.Comments = p.Comments; m.Shares = p.Shares; m.InsightsAt = DateTime.UtcNow;
                        if (linked.TryGetValue(p.Id, out var pid)) m.PostId = pid;
                        if (p.Comments == 0) continue;
                        var comments = await facebook.CommentsAsync(p.Id, token, ct);
                        var cids = comments.Select(c => c.Id).ToList();
                        var knownC = await db.InstagramComments.Where(c => cids.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
                        foreach (var c in comments.Where(c => !knownC.Contains(c.Id)))
                        {
                            var own = c.Author == ch.Name;
                            db.InstagramComments.Add(new InstagramComment
                            {
                                Id = c.Id, Platform = "facebook", MediaId = p.Id, ParentId = c.ParentId, Username = Trim(c.Author, 64), Text = Trim(c.Text, 2200),
                                At = c.At, Hidden = c.Hidden, IsOwn = own, Done = own
                            });
                            if (!own) added++;
                        }
                        var answered = comments.Where(c => c.ParentId is not null && c.Author == ch.Name).Select(c => c.ParentId!).ToHashSet();
                        if (answered.Count > 0)
                            await db.InstagramComments.Where(c => answered.Contains(c.Id)).ExecuteUpdateAsync(u => u.SetProperty(c => c.Done, true), ct);
                    }
                }
                else if (ch.Platform == SocialPlatform.Threads)
                {
                    var published = await db.SocialPostTargets.Include(t => t.Post).Where(t => t.ChannelId == ch.Id && t.ExternalId != null && t.PublishedAt > DateTime.UtcNow.AddDays(-30)).ToListAsync(ct);
                    foreach (var t in published)
                    {
                        var ins = await threads.InsightsAsync(t.ExternalId!, token, ct);
                        var m = await db.InstagramMedia.FirstOrDefaultAsync(x => x.Id == t.ExternalId, ct);
                        if (m is null) db.InstagramMedia.Add(m = new InstagramMedia { Id = t.ExternalId!, Platform = "threads", MediaType = "THREADS_POST" });
                        m.PostId = t.PostId; m.Caption = CaptionFor(t, t.Post!); m.Permalink = t.Permalink; m.PostedAt = t.PublishedAt ?? DateTime.UtcNow;
                        m.Views = ins.GetValueOrDefault("views"); m.Likes = ins.GetValueOrDefault("likes"); m.Comments = ins.GetValueOrDefault("replies");
                        m.Shares = ins.GetValueOrDefault("reposts") + ins.GetValueOrDefault("quotes"); m.InsightsAt = DateTime.UtcNow;
                    }
                }
                ch.LastSyncAt = DateTime.UtcNow;
                ch.LastError = null;
            }
            catch (InstagramApiException ex) { ch.LastError = ex.Message; error = $"{SocialPlatforms.Get(ch.Platform).Label}: {ex.Message}"; }
        }
        await db.SaveChangesAsync(ct);
        return (added, error);
    }

    public async Task<(bool Ok, string Message)> ReplyAsync(InstagramComment c, string text)
    {
        var (ch, token) = await FacebookChannelAsync();
        if (ch is null || token is null) return (false, "Strona na Facebooku nie jest połączona.");
        try
        {
            var id = await facebook.ReplyAsync(c.ParentId ?? c.Id, token, text);
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.InstagramComments.Where(x => x.Id == c.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Done, true));
            if (id is not null)
            {
                db.InstagramComments.Add(new InstagramComment { Id = id, Platform = "facebook", MediaId = c.MediaId, ParentId = c.ParentId ?? c.Id, Username = Trim(ch.Name, 64), Text = text, At = DateTime.UtcNow, IsOwn = true, Done = true });
                await db.SaveChangesAsync();
            }
            return (true, "Odpowiedź opublikowana na Facebooku.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    public async Task<(bool Ok, string Message)> HideAsync(InstagramComment c, bool hide)
    {
        var (ch, token) = await FacebookChannelAsync();
        if (ch is null || token is null) return (false, "Strona na Facebooku nie jest połączona.");
        try
        {
            await facebook.HideAsync(c.Id, token, hide);
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.InstagramComments.Where(x => x.Id == c.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Hidden, hide).SetProperty(x => x.Done, true));
            return (true, hide ? "Komentarz ukryty." : "Komentarz znów widoczny.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    private async Task<(SocialChannel? Channel, string? Token)> FacebookChannelAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ch = await db.SocialChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Platform == SocialPlatform.Facebook && c.Api && c.Enabled);
        return (ch, ch is null ? null : Unprotect(ch.Token));
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
