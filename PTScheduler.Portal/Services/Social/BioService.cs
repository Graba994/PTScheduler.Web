using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Strona „link w bio” (hantly.pl/linki): nagłówek, przyciski i ostatnie posty. Przyciski prowadzące na stronę Hantly
/// idą przez linki śledzące (kanał „bio”), więc w „Linki i wyniki” widać, który przycisk przynosi rejestracje.
/// Wejścia na samą stronę liczy stały link „Strona link w bio — wejścia”.
/// </summary>
public class BioService(IDbContextFactory<PortalDbContext> dbFactory, SiteSettingsService settings, LinkService links)
{
    public const string Path = LinkService.BioViewsTarget;
    public const string DefaultTitle = "Hantly";
    public const string DefaultText = "Aplikacja dla trenerów personalnych: zapisy, plany treningowe i płatności w jednym miejscu — pod Twoją marką.";

    public static readonly (string Icon, string Label)[] Icons =
    [
        ("bi-rocket-takeoff", "Start"), ("bi-arrow-right", "Strzałka"), ("bi-phone", "Telefon"), ("bi-stars", "AI"),
        ("bi-tag", "Cennik"), ("bi-play-circle", "Film"), ("bi-chat-dots", "Kontakt"), ("bi-gift", "Prezent"),
        ("bi-instagram", "Instagram"), ("bi-facebook", "Facebook"), ("bi-youtube", "YouTube"), ("bi-tiktok", "TikTok"), ("bi-link-45deg", "Link")
    ];

    public sealed record Settings(string Title, string Text, bool ShowPosts);
    public sealed record Item(BioItem Row, string Href, bool External, int Clicks);
    public sealed record Post(string Image, string Href, string Title);
    public sealed record Page(Settings Settings, List<Item> Items, List<Post> Posts);

    public string Url => links.Base + Path;

    public async Task<Settings> SettingsAsync()
    {
        var title = await settings.GetAsync(K.BioTitle);
        var text = await settings.GetAsync(K.BioText);
        var posts = await settings.GetAsync(K.BioShowPosts);
        return new Settings(string.IsNullOrWhiteSpace(title) ? DefaultTitle : title, string.IsNullOrWhiteSpace(text) ? DefaultText : text, posts != "0");
    }

    public async Task SaveSettingsAsync(string title, string text, bool showPosts)
    {
        await settings.SetAsync(K.BioTitle, Clip(title.Trim(), 60));
        await settings.SetAsync(K.BioText, Clip(text.Trim(), 300));
        await settings.SetAsync(K.BioShowPosts, showPosts ? "1" : "0");
    }

    /// <summary>Pełny adres https:// albo ścieżka na stronie Hantly; inne odrzucamy.</summary>
    public static string? NormalizeTarget(string? target)
    {
        var t = (target ?? "").Trim();
        if (t.StartsWith('/')) return LinkService.SafeTarget(t) == t ? t : null;
        return Uri.TryCreate(t, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && t.Length <= 500 ? u.ToString() : null;
    }

    public async Task<List<Item>> ItemsAsync(bool enabledOnly)
    {
        await EnsureDefaultsAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.BioItems.AsNoTracking();
        if (enabledOnly) q = q.Where(i => i.Enabled);
        var rows = await q.OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync();
        var ids = rows.Where(r => r.LinkId != null).Select(r => r.LinkId!.Value).ToList();
        var linkRows = await db.TrackedLinks.AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id);
        return rows.Select(r =>
        {
            var external = !r.Target.StartsWith('/');
            var link = r.LinkId is int id ? linkRows.GetValueOrDefault(id) : null;
            var href = external ? r.Target : link is not null ? $"/go/{link.Code}" : r.Target;
            return new Item(r, href, external, link?.Clicks ?? 0);
        }).ToList();
    }

    /// <summary>Pierwsze wejście: gotowe przyciski, żeby strona od razu miała sens.</summary>
    private async Task EnsureDefaultsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.BioItems.AnyAsync()) return;
        var defaults = new (string Title, string? Sub, string Target, string Icon, bool Highlight)[]
        {
            ("Zbuduj swoją aplikację za darmo", "Gotowa w kilka minut", "/register", "bi-rocket-takeoff", true),
            ("Zobacz, co potrafi", "Zapisy, plany, płatności, asystent AI", "/#funkcje", "bi-phone", false),
            ("Cennik", null, "/#cennik", "bi-tag", false),
            ("Dla trenera personalnego", null, "/aplikacja-dla-trenera-personalnego", "bi-arrow-right", false)
        };
        var order = 0;
        foreach (var d in defaults)
        {
            var link = await links.CreateAsync($"Bio: {d.Title}", "bio", d.Target);
            db.BioItems.Add(new BioItem { Title = d.Title, Subtitle = d.Sub, Target = d.Target, Icon = d.Icon, Highlight = d.Highlight, Order = order++, LinkId = link.Link?.Id });
        }
        await db.SaveChangesAsync();
    }

    public async Task<(bool Ok, string Message)> SaveItemAsync(int? id, string title, string? subtitle, string target, string icon, bool highlight, bool enabled)
    {
        title = Clip((title ?? "").Trim(), 80);
        if (title.Length == 0) return (false, "Wpisz tekst przycisku.");
        if (NormalizeTarget(target) is not { } normalized) return (false, "Adres: ścieżka na stronie Hantly (np. /register) albo pełny adres zaczynający się od https://.");
        if (!Icons.Any(i => i.Icon == icon)) icon = "bi-arrow-right";
        subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : Clip(subtitle.Trim(), 120);
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = id is int x ? await db.BioItems.FirstOrDefaultAsync(i => i.Id == x) : null;
        if (row is null)
        {
            row = new BioItem { Order = (await db.BioItems.MaxAsync(i => (int?)i.Order) ?? -1) + 1 };
            db.BioItems.Add(row);
        }
        row.Title = title; row.Subtitle = subtitle; row.Icon = icon; row.Highlight = highlight; row.Enabled = enabled;
        if (row.Target != normalized || (row.LinkId is null && normalized.StartsWith('/')))
        {
            row.Target = normalized;
            if (normalized.StartsWith('/'))
            {
                // Nowy cel — nowy link, żeby stare wyniki zostały przy starym celu.
                var (link, _) = await links.CreateAsync($"Bio: {title}", "bio", normalized);
                row.LinkId = link?.Id;
            }
            else row.LinkId = null;
        }
        else if (row.LinkId is int lid) await links.UpdateAsync(lid, $"Bio: {title}", row.Target);
        await db.SaveChangesAsync();
        return (true, "Zapisano przycisk.");
    }

    public async Task DeleteItemAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.BioItems.FirstOrDefaultAsync(i => i.Id == id);
        if (row is null) return;
        db.BioItems.Remove(row);
        await db.SaveChangesAsync();
        if (row.LinkId is int lid) await links.ArchiveAsync(lid, true);
    }

    public async Task MoveAsync(int id, int delta)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.BioItems.OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync();
        var i = rows.FindIndex(r => r.Id == id);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= rows.Count) return;
        (rows[i], rows[j]) = (rows[j], rows[i]);
        for (var k = 0; k < rows.Count; k++) rows[k].Order = k;
        await db.SaveChangesAsync();
    }

    /// <summary>Publiczna strona: ustawienia, włączone przyciski i 6 ostatnich opublikowanych postów z grafiką.</summary>
    public async Task<Page> PageAsync()
    {
        var s = await SettingsAsync();
        var items = await ItemsAsync(enabledOnly: true);
        var posts = new List<Post>();
        if (s.ShowPosts)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var recent = await db.InstagramPosts.AsNoTracking()
                .Where(p => p.Status == InstagramPostStatus.Published && p.Permalink != null && p.Kind != InstagramPostKind.Story)
                .OrderByDescending(p => p.PublishedAt).Take(12)
                .Select(p => new { p.Id, p.Title, p.Permalink }).ToListAsync();
            var ids = recent.Select(p => p.Id).ToList();
            var images = (await db.InstagramAssets.AsNoTracking().Where(a => ids.Contains(a.PostId) && a.Kind == "image")
                    .Select(a => new { a.PostId, a.Order, a.Token }).ToListAsync())
                .GroupBy(a => a.PostId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.Order).First().Token);
            posts = recent.Where(p => images.ContainsKey(p.Id)).Take(6)
                .Select(p => new Post($"/media/ig/{images[p.Id]}.jpg", p.Permalink!, p.Title)).ToList();
        }
        return new Page(s, items, posts);
    }

    /// <summary>Wejście na stronę (bez botów) — licznik stałego linku „bio”.</summary>
    public async Task CountViewAsync(string? userAgent)
    {
        if (LinkService.IsBot(userAgent)) return;
        var link = await links.FixedAsync("bio", "Strona link w bio — wejścia", Path);
        await links.CountAsync(link.Id);
    }

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}
