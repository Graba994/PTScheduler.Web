using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Linki śledzące: hantly.pl/go/{kod} → strona Hantly z ?src={kod}. Kreator zapisuje kod w trenerze (<see cref="Tenant.SignupSource"/>),
/// więc widać, który post i kanał przyniósł rejestracje i płatne konta. Bez cookie i bez danych osób — tylko liczniki.
/// W opisie posta „{link}” zamienia się na link danego kanału (na Instagramie — „link w bio”, bo tam linki w opisie nie działają).
/// </summary>
public partial class LinkService(IDbContextFactory<PortalDbContext> dbFactory, IConfiguration config)
{
    public const string Placeholder = "{link}";
    public const string InstagramText = "link w bio";
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public static readonly (string Key, string Label, string Icon)[] Channels =
    [
        ("instagram", "Instagram", "bi-instagram"), ("facebook", "Facebook", "bi-facebook"), ("threads", "Threads", "bi-threads"),
        ("tiktok", "TikTok", "bi-tiktok"), ("youtube", "YouTube", "bi-youtube"), ("linkedin", "LinkedIn", "bi-linkedin"),
        ("x", "X", "bi-twitter-x"), ("pinterest", "Pinterest", "bi-pinterest"), ("bio", "Link w bio", "bi-link-45deg"),
        ("reklama", "Reklama", "bi-megaphone"), ("inne", "Inne", "bi-signpost")
    ];

    public static readonly (string Path, string Label)[] Targets =
    [
        ("/register", "Rejestracja (kreator)"), ("/", "Strona główna"), ("/#cennik", "Cennik"), ("/#funkcje", "Funkcje"),
        ("/aplikacja-dla-trenera-personalnego", "Dla trenera personalnego")
    ];

    public static string ChannelLabel(string key) => Channels.FirstOrDefault(c => c.Key == key).Label ?? key;
    public static string ChannelIcon(string key) => Channels.FirstOrDefault(c => c.Key == key).Icon ?? "bi-link";
    public static string KeyOf(SocialPlatform p) => p.ToString().ToLowerInvariant();

    public string Base => PortalUrls.Public(config);
    public string Url(string code) => $"{Base}/go/{code}";
    /// <summary>Krótki adres do wklejenia (bez https://).</summary>
    public string ShortUrl(string code) => Url(code).Replace("https://", "").Replace("http://", "");

    [GeneratedRegex("^[a-z0-9]{4,12}$")] public static partial Regex CodeRx();
    public static bool ValidCode(string? code) => code is not null && CodeRx().IsMatch(code);

    /// <summary>Podglądy linków (Facebook, WhatsApp, Slack…), wyszukiwarki i skrypty — nie liczymy ich jako kliknięć.</summary>
    [GeneratedRegex(@"bot|crawl|spider|slurp|facebookexternalhit|meta-externalagent|facebookcatalog|embedly|preview|whatsapp|telegram|discord|skype|slack|linkedinbot|twitterbot|pinterestbot|vkshare|headless|curl|wget|python|go-http|okhttp|java/|httpclient", RegexOptions.IgnoreCase)]
    private static partial Regex BotRx();
    public static bool IsBot(string? userAgent) => string.IsNullOrWhiteSpace(userAgent) || BotRx().IsMatch(userAgent);

    /// <summary>Bezpieczny cel: tylko ścieżka na stronie Hantly (bez przekierowań na obce adresy).</summary>
    public static string SafeTarget(string? target)
    {
        var t = (target ?? "").Trim();
        if (t.Length == 0 || t.Length > 300 || !t.StartsWith('/') || t.StartsWith("//") || t.StartsWith("/\\") || t.Contains("://")) return "/register";
        return t;
    }

    /// <summary>Adres przekierowania: cel + ?src=kod (przed #kotwicą).</summary>
    public static string Redirect(string target, string code)
    {
        target = SafeTarget(target);
        var hash = target.IndexOf('#');
        var (path, anchor) = hash >= 0 ? (target[..hash], target[hash..]) : (target, "");
        if (path.Length == 0) path = "/";
        return $"{path}{(path.Contains('?') ? '&' : '?')}src={code}{anchor}";
    }

    private static string NewCode()
    {
        Span<char> c = stackalloc char[6];
        for (var i = 0; i < c.Length; i++) c[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(c);
    }

    private static async Task<string> FreeCodeAsync(PortalDbContext db)
    {
        for (var i = 0; ; i++)
        {
            var code = NewCode();
            if (!await db.TrackedLinks.AnyAsync(l => l.Code == code) || i > 20) return code;
        }
    }

    // ── Tworzenie ────────────────────────────────────────────────────

    /// <summary>Link posta dla kanału (tworzony przy pierwszym użyciu — ten sam przy kolejnych).</summary>
    public async Task<TrackedLink> ForPostAsync(int postId, string channel, string? title = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = await db.TrackedLinks.FirstOrDefaultAsync(l => l.PostId == postId && l.Channel == channel);
        if (link is not null) return link;
        title ??= await db.InstagramPosts.Where(p => p.Id == postId).Select(p => p.Title).FirstOrDefaultAsync();
        link = new TrackedLink
        {
            Code = await FreeCodeAsync(db), Channel = channel, PostId = postId,
            Label = string.IsNullOrWhiteSpace(title) ? $"Post #{postId}" : Clip(title, 120), Target = "/register"
        };
        db.TrackedLinks.Add(link);
        await db.SaveChangesAsync();
        return link;
    }

    /// <summary>Linki posta (po kanałach) — bez tworzenia nowych.</summary>
    public async Task<Dictionary<string, TrackedLink>> OfPostAsync(int postId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.TrackedLinks.AsNoTracking().Where(l => l.PostId == postId).ToListAsync())
            .GroupBy(l => l.Channel).ToDictionary(g => g.Key, g => g.First());
    }

    public async Task<(TrackedLink? Link, string? Error)> CreateAsync(string label, string channel, string target)
    {
        label = (label ?? "").Trim();
        if (label.Length == 0) return (null, "Nazwij link, np. „Reklama Meta — listopad”.");
        if (!Channels.Any(c => c.Key == channel)) channel = "inne";
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = new TrackedLink { Code = await FreeCodeAsync(db), Channel = channel, Label = Clip(label, 120), Target = SafeTarget(target) };
        db.TrackedLinks.Add(link);
        await db.SaveChangesAsync();
        return (link, null);
    }

    /// <summary>Stały link danego rodzaju bez posta (np. „bio”) — tworzony raz.</summary>
    public async Task<TrackedLink> FixedAsync(string channel, string label, string target)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var safe = SafeTarget(target);
        var link = await db.TrackedLinks.FirstOrDefaultAsync(l => l.PostId == null && l.Channel == channel && l.Target == safe && !l.Archived);
        if (link is not null) return link;
        link = new TrackedLink { Code = await FreeCodeAsync(db), Channel = channel, Label = label, Target = safe };
        db.TrackedLinks.Add(link);
        await db.SaveChangesAsync();
        return link;
    }

    public async Task UpdateAsync(int id, string label, string target)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var safe = SafeTarget(target);
        var l = Clip((label ?? "").Trim(), 120);
        await db.TrackedLinks.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Target, safe)
            .SetProperty(x => x.Label, x => l.Length > 0 ? l : x.Label));
    }

    public async Task ArchiveAsync(int id, bool archived)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.TrackedLinks.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Archived, archived));
    }

    /// <summary>Wstawia link kanału w miejsce „{link}”. Bez „{link}” w opisie nic nie tworzy.</summary>
    public async Task<string> ExpandAsync(string caption, string channel, int postId, string? title = null)
    {
        if (string.IsNullOrEmpty(caption) || !caption.Contains(Placeholder, StringComparison.OrdinalIgnoreCase)) return caption ?? "";
        if (channel == "instagram") return ForInstagram(caption);
        var link = await ForPostAsync(postId, channel, title);
        return Regex.Replace(caption, Regex.Escape(Placeholder), ShortUrl(link.Code), RegexOptions.IgnoreCase);
    }

    /// <summary>Instagram: linki w opisie nie są klikalne — „{link}” → „link w bio”.</summary>
    public static string ForInstagram(string? caption) =>
        string.IsNullOrEmpty(caption) ? caption ?? "" : Regex.Replace(caption, Regex.Escape(Placeholder), InstagramText, RegexOptions.IgnoreCase);

    // ── Kliknięcia ───────────────────────────────────────────────────

    /// <summary>Liczy kliknięcie (bez botów) i zwraca adres przekierowania; null — nie ma takiego linku.</summary>
    public async Task<string?> ClickAsync(string code, string? userAgent)
    {
        if (!ValidCode(code)) return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = await db.TrackedLinks.AsNoTracking().Where(l => l.Code == code).Select(l => new { l.Id, l.Target }).FirstOrDefaultAsync();
        if (link is null) return null;
        if (!IsBot(userAgent)) await CountAsync(link.Id);
        return Redirect(link.Target, code);
    }

    /// <summary>+1 kliknięcie (licznik linku i licznik dnia).</summary>
    public async Task CountAsync(int linkId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        await db.TrackedLinks.Where(l => l.Id == linkId).ExecuteUpdateAsync(u => u.SetProperty(l => l.Clicks, l => l.Clicks + 1).SetProperty(l => l.LastClickAt, now));
        var day = DateOnly.FromDateTime(InstagramRules.ToWarsaw(now));
        if (await db.TrackedLinkDays.Where(d => d.LinkId == linkId && d.Day == day).ExecuteUpdateAsync(u => u.SetProperty(d => d.Clicks, d => d.Clicks + 1)) > 0) return;
        try
        {
            db.TrackedLinkDays.Add(new TrackedLinkDay { LinkId = linkId, Day = day, Clicks = 1 });
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            await using var retry = await dbFactory.CreateDbContextAsync();
            await retry.TrackedLinkDays.Where(d => d.LinkId == linkId && d.Day == day).ExecuteUpdateAsync(u => u.SetProperty(d => d.Clicks, d => d.Clicks + 1));
        }
    }

    // ── Wyniki ───────────────────────────────────────────────────────

    public sealed record Row(TrackedLink Link, string? PostTitle, int Clicks, int Signups, int Paid);
    public sealed record ChannelRow(string Channel, int Clicks, int Signups, int Paid, int Links);
    /// <summary><c>BioViews</c> — wejścia na stronę „link w bio” (liczone osobno, żeby nie dublować kliknięć jej przycisków).</summary>
    public sealed record Report(List<Row> Rows, List<ChannelRow> Channels, int Clicks, int Signups, int Paid, int SignupsTotal, List<(DateOnly Day, int Clicks)> Daily, int BioViews);
    public const string BioViewsTarget = "/linki";

    /// <summary>
    /// Wyniki za ostatnie <paramref name="days"/> dni: kliknięcia z okresu, rejestracje trenerów z okresu (z kodem linku)
    /// i ilu z nich już zapłaciło. <c>SignupsTotal</c> — wszystkie rejestracje w okresie (także bez linku), dla porównania.
    /// </summary>
    public async Task<Report> ReportAsync(int days, bool includeArchived = false)
    {
        days = Math.Clamp(days, 1, 3650);
        var since = DateTime.UtcNow.AddDays(-days);
        var sinceDay = DateOnly.FromDateTime(InstagramRules.ToWarsaw(since));
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.TrackedLinks.AsNoTracking().Where(l => includeArchived || !l.Archived).ToListAsync();
        var clicks = await db.TrackedLinkDays.AsNoTracking().Where(d => d.Day >= sinceDay).ToListAsync();
        var viewIds = all.Where(IsBioViews).Select(l => l.Id).ToHashSet();
        var bioViews = clicks.Where(c => viewIds.Contains(c.LinkId)).Sum(c => c.Clicks);
        var links = all.Where(l => !viewIds.Contains(l.Id)).ToList();
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.CreatedAt >= since && t.Status != TenantStatus.Destroyed)
            .Select(t => new { t.SignupSource, Paid = t.FirstPaidAt != null }).ToListAsync();
        var postIds = links.Where(l => l.PostId != null).Select(l => l.PostId!.Value).Distinct().ToList();
        var titles = await db.InstagramPosts.AsNoTracking().Where(p => postIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title);

        var rows = links.Select(l =>
        {
            var mine = tenants.Where(t => t.SignupSource == l.Code).ToList();
            return new Row(l, l.PostId is int pid ? titles.GetValueOrDefault(pid) : null,
                clicks.Where(c => c.LinkId == l.Id).Sum(c => c.Clicks), mine.Count, mine.Count(t => t.Paid));
        }).OrderByDescending(r => r.Signups).ThenByDescending(r => r.Clicks).ThenByDescending(r => r.Link.CreatedAt).ToList();
        var channels = rows.GroupBy(r => r.Link.Channel)
            .Select(g => new ChannelRow(g.Key, g.Sum(r => r.Clicks), g.Sum(r => r.Signups), g.Sum(r => r.Paid), g.Count()))
            .OrderByDescending(c => c.Signups).ThenByDescending(c => c.Clicks).ToList();
        var ids = links.Select(l => l.Id).ToHashSet();
        var daily = clicks.Where(c => ids.Contains(c.LinkId)).GroupBy(c => c.Day).Select(g => (g.Key, g.Sum(c => c.Clicks))).OrderBy(x => x.Key).ToList();
        return new Report(rows, channels, rows.Sum(r => r.Clicks), rows.Sum(r => r.Signups), rows.Sum(r => r.Paid), tenants.Count, daily, bioViews);
    }

    private static bool IsBioViews(TrackedLink l) => l.PostId == null && l.Channel == "bio" && l.Target == BioViewsTarget;

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}
