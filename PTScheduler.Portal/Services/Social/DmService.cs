using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Wspólna skrzynka wiadomości prywatnych (Instagram + Messenger strony) i reguły „komentarz → wiadomość”.
/// Odpowiedzi pisze człowiek (AI tylko podpowiada); reguły wysyłają gotowy tekst ustawiony przez administratora.
/// Do AI idą same treści wiadomości bez nazw, e-maili i numerów telefonów. Wiadomości starsze niż 180 dni są usuwane.
/// </summary>
public partial class DmService(
    IDbContextFactory<PortalDbContext> dbFactory,
    MessagesClient client,
    InstagramService ig,
    InstagramAi ai,
    LinkService links,
    SiteSettingsService settings,
    IDataProtectionProvider dataProtection,
    ILogger<DmService> logger)
{
    public const int ReplyWindowHours = 24;
    public const int RetentionDays = 180;
    /// <summary>Instagram pozwala na prywatną odpowiedź do 7 dni od komentarza.</summary>
    public const int PrivateReplyDays = 7;
    private const int MaxRulesPerRun = 20;

    private IDataProtector Secrets => dataProtection.CreateProtector("instagram");
    private string? Unprotect(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); } catch (CryptographicException) { return null; }
    }

    // ── Skrzynki ─────────────────────────────────────────────────────

    /// <summary>Połączone skrzynki: Instagram (przez stronę albo logowanie Instagramem) i Messenger każdej strony.</summary>
    public async Task<List<MessagesClient.Box>> BoxesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var boxes = new List<MessagesClient.Box>();
        var pages = await db.SocialChannels.AsNoTracking().Where(c => c.Platform == SocialPlatform.Facebook && c.Enabled && c.ExternalId != null).ToListAsync();
        if (await db.InstagramAccounts.AsNoTracking().OrderBy(a => a.Id).FirstOrDefaultAsync() is { } a && Unprotect(a.Token) is { } igToken)
        {
            if (a.Provider == "facebook")
            {
                var pageId = a.PageId ?? pages.FirstOrDefault()?.ExternalId;
                if (pageId is not null) boxes.Add(MessagesClient.Box.InstagramViaPage(client.FacebookGraph, pageId, a.IgUserId, igToken));
            }
            else boxes.Add(MessagesClient.Box.InstagramLogin(client.InstagramGraph, a.IgUserId, igToken));
        }
        foreach (var p in pages)
            if (Unprotect(p.Token) is { } t) boxes.Add(MessagesClient.Box.Messenger(client.FacebookGraph, p.ExternalId!, t));
        return boxes;
    }

    /// <summary>Błąd ostatniego pobrania (np. brak uprawnień do wiadomości) — pokazujemy w skrzynce.</summary>
    public async Task<string?> LastErrorAsync() => await settings.GetAsync(K.DmLastError) is { Length: > 0 } e ? e : null;

    // ── Pobieranie ───────────────────────────────────────────────────

    public async Task<(int New, string? Error)> SyncAsync(CancellationToken ct = default)
    {
        var boxes = await BoxesAsync();
        var added = 0;
        var errors = new List<string>();
        foreach (var box in boxes)
        {
            try
            {
                var conversations = await client.ConversationsAsync(box, ct);
                added += await StoreAsync(box, conversations, ct);
            }
            catch (InstagramApiException ex)
            {
                errors.Add($"{(box.Platform == "facebook" ? "Messenger" : "Instagram")}: {ex.Message}");
                logger.LogInformation("Wiadomości {Platform}: {Message}", box.Platform, ex.Message);
            }
        }
        var error = errors.Count > 0 ? string.Join(" ", errors) : null;
        await settings.SetAsync(K.DmLastError, error ?? "");
        return (added, boxes.Count == 0 ? "Połącz Instagram albo stronę na Facebooku w zakładce Kanały." : error);
    }

    /// <summary>Zapis rozmów: nowe wiadomości, stan „nieprzeczytane” i „do odpowiedzi” po wiadomości od osoby.</summary>
    public async Task<int> StoreAsync(MessagesClient.Box box, List<MessagesClient.Conversation> conversations, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var added = 0;
        foreach (var c in conversations)
        {
            var thread = await db.SocialThreads.FirstOrDefaultAsync(t => t.Platform == box.Platform && t.ExternalId == c.Id, ct);
            if (thread is null)
            {
                thread = new SocialThread { Platform = box.Platform, ExternalId = c.Id, Done = true };
                db.SocialThreads.Add(thread);
                await db.SaveChangesAsync(ct);
            }
            thread.ParticipantId = c.ParticipantId;
            thread.ParticipantName = Clip(c.ParticipantName, 120);
            var ids = c.Messages.Select(m => m.Id).ToList();
            var known = await db.SocialMessages.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct);
            foreach (var m in c.Messages.Where(m => m.Id.Length > 0 && !known.Contains(m.Id)))
            {
                var inbound = m.FromId != box.OwnId;
                db.SocialMessages.Add(new SocialMessage
                {
                    Id = Clip(m.Id, 200), ThreadId = thread.Id, Inbound = inbound, At = m.At, HasAttachment = m.HasAttachment,
                    Text = Clip(m.Text.Length > 0 ? m.Text : m.HasAttachment ? "[załącznik]" : "", 4000)
                });
                added++;
                if (inbound && m.At >= (thread.LastInboundAt ?? DateTime.MinValue))
                {
                    thread.LastInboundAt = m.At;
                    thread.Unread = true;
                    thread.Done = false;
                    thread.Suggestion = null;
                }
            }
            if (c.Messages.Count > 0)
            {
                var last = c.Messages[^1];
                if (last.At >= thread.LastMessageAt)
                {
                    thread.LastMessageAt = last.At;
                    thread.Snippet = Clip(last.Text.Length > 0 ? last.Text : "[załącznik]", 200);
                    // Ostatnie słowo nasze (np. odpowiedź z telefonu) — rozmowa obsłużona.
                    if (last.FromId == box.OwnId) { thread.Done = true; thread.Unread = false; }
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    // ── Skrzynka w panelu ────────────────────────────────────────────

    public async Task<List<SocialThread>> ThreadsAsync(bool openOnly)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.SocialThreads.AsNoTracking();
        if (openOnly) q = q.Where(t => !t.Done);
        return await q.OrderByDescending(t => t.LastMessageAt).Take(100).ToListAsync();
    }

    public async Task<int> OpenCountAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SocialThreads.CountAsync(t => !t.Done);
    }

    public async Task<List<SocialMessage>> MessagesAsync(int threadId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.SocialThreads.FirstOrDefaultAsync(t => t.Id == threadId && t.Unread) is { } unread)
        {
            unread.Unread = false;
            await db.SaveChangesAsync();
        }
        return await db.SocialMessages.AsNoTracking().Where(m => m.ThreadId == threadId).OrderBy(m => m.At).ToListAsync();
    }

    public static bool CanReply(SocialThread t) => t.LastInboundAt is { } at && DateTime.UtcNow - at < TimeSpan.FromHours(ReplyWindowHours);

    public async Task SetDoneAsync(int threadId, bool done)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.SocialThreads.Where(t => t.Id == threadId).ExecuteUpdateAsync(u => u.SetProperty(t => t.Done, done).SetProperty(t => t.Unread, false));
    }

    public async Task<(bool Ok, string Message)> ReplyAsync(int threadId, string text, CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return (false, "Wpisz odpowiedź.");
        if (text.Length > 1000) return (false, "Wiadomość może mieć najwyżej 1000 znaków.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.SocialThreads.FirstOrDefaultAsync(x => x.Id == threadId, ct);
        if (t is null) return (false, "Nie ma takiej rozmowy.");
        if (!CanReply(t)) return (false, $"Minęło {ReplyWindowHours} h od ostatniej wiadomości tej osoby — Meta nie pozwala już odpowiedzieć z Portalu. Odpisz w aplikacji Instagram/Messenger.");
        var box = (await BoxesAsync()).FirstOrDefault(b => b.Platform == t.Platform);
        if (box is null) return (false, "Skrzynka nie jest połączona — sprawdź zakładkę Kanały.");
        if (text.Contains(LinkService.Placeholder, StringComparison.OrdinalIgnoreCase))
        {
            var link = await links.FixedAsync("dm", "Wiadomości prywatne — odpowiedzi", "/register");
            text = Regex.Replace(text, Regex.Escape(LinkService.Placeholder), links.Url(link.Code), RegexOptions.IgnoreCase);
        }
        try
        {
            var id = await client.SendAsync(box, t.ParticipantId, text, ct);
            db.SocialMessages.Add(new SocialMessage { Id = id ?? $"local-{Guid.NewGuid():N}", ThreadId = t.Id, Inbound = false, Text = Clip(text, 4000), At = DateTime.UtcNow });
            t.LastMessageAt = DateTime.UtcNow;
            t.Snippet = Clip(text, 200);
            t.Done = true;
            t.Unread = false;
            t.Suggestion = null;
            await db.SaveChangesAsync(ct);
            return (true, "Wysłano.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    // ── Podpowiedź AI ────────────────────────────────────────────────

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")] private static partial Regex EmailRx();
    [GeneratedRegex(@"(?<!\w)(\+?\d[\d \-]{7,}\d)")] private static partial Regex PhoneRx();

    /// <summary>Bez e-maili i numerów telefonów — tak treść idzie do AI.</summary>
    public static string Scrub(string text) => PhoneRx().Replace(EmailRx().Replace(text, "[e-mail]"), "[telefon]");

    public async Task<(string? Text, string? Error)> SuggestAsync(int threadId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.SocialThreads.FirstOrDefaultAsync(x => x.Id == threadId, ct);
        if (t is null) return (null, "Nie ma takiej rozmowy.");
        var last = await db.SocialMessages.AsNoTracking().Where(m => m.ThreadId == threadId).OrderByDescending(m => m.At).Take(8).ToListAsync(ct);
        if (!last.Any(m => m.Inbound)) return (null, "Brak wiadomości od tej osoby.");
        var sb = new StringBuilder();
        foreach (var m in last.OrderBy(m => m.At)) sb.AppendLine($"{(m.Inbound ? "Osoba" : "Hantly")}: {Scrub(m.Text)}");
        var (text, error) = await ai.DmReplyAsync(t.Platform == "facebook" ? "Messenger" : "Instagram", sb.ToString(), ct);
        if (text is not null)
        {
            t.Suggestion = Clip(text, 1000);
            await db.SaveChangesAsync(ct);
        }
        return (text, error);
    }

    // ── Reguły „komentarz → wiadomość” ──────────────────────────────

    public async Task<List<CommentRule>> RulesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.CommentRules.AsNoTracking().OrderBy(r => r.Keyword).ToListAsync();
    }

    public static string NormalizeKeyword(string? k) => Regex.Replace((k ?? "").Trim(), @"\s+", " ").ToUpperInvariant();

    public async Task<(bool Ok, string Message)> SaveRuleAsync(int? id, string keyword, string message, string? publicReply, string target, bool enabled)
    {
        keyword = NormalizeKeyword(keyword);
        message = (message ?? "").Trim();
        publicReply = string.IsNullOrWhiteSpace(publicReply) ? null : publicReply.Trim();
        if (keyword.Length is < 2 or > 40) return (false, "Słowo-klucz: od 2 do 40 znaków, np. PLAN.");
        if (message.Length is < 5 or > 1000) return (false, "Wiadomość: od 5 do 1000 znaków.");
        if (publicReply?.Length > 300) return (false, "Odpowiedź pod komentarzem: najwyżej 300 znaków.");
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.CommentRules.AnyAsync(r => r.Keyword == keyword && r.Id != (id ?? 0))) return (false, $"Reguła dla „{keyword}” już jest.");
        var rule = id is int x ? await db.CommentRules.FirstOrDefaultAsync(r => r.Id == x) : null;
        if (rule is null) db.CommentRules.Add(rule = new CommentRule());
        var safe = LinkService.SafeTarget(target);
        if (rule.LinkId is null || rule.Target != safe)
        {
            var (link, _) = await links.CreateAsync($"Komentarz → wiadomość: {keyword}", "dm", safe);
            rule.LinkId = link?.Id;
        }
        (rule.Keyword, rule.Message, rule.PublicReply, rule.Target, rule.Enabled) = (keyword, message, publicReply, safe, enabled);
        await db.SaveChangesAsync();
        return (true, "Zapisano regułę. Działa dla nowych komentarzy.");
    }

    public async Task DeleteRuleAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.CommentRules.Where(r => r.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Czy komentarz zawiera słowo-klucz (całe słowo, bez względu na wielkość liter).</summary>
    public static bool Matches(string comment, string keyword) =>
        Regex.IsMatch(comment ?? "", $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(keyword)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Nowe komentarze ze słowem-kluczem → prywatna wiadomość do autora (+ opcjonalna odpowiedź pod komentarzem).
    /// Tylko komentarze po utworzeniu reguły i nie starsze niż 7 dni; jeden komentarz — jedna wiadomość.
    /// </summary>
    public async Task<int> ApplyRulesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rules = await db.CommentRules.Where(r => r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return 0;
        var oldest = rules.Min(r => r.CreatedAt);
        var since = DateTime.UtcNow.AddDays(-PrivateReplyDays);
        var comments = await db.InstagramComments
            .Where(c => !c.IsOwn && !c.Hidden && c.PrivateReplyAt == null && c.RuleId == null && c.At >= since && c.At >= oldest)
            .OrderBy(c => c.At).Take(200).ToListAsync(ct);
        var boxes = await BoxesAsync();
        var linkIds = rules.Where(r => r.LinkId != null).Select(r => r.LinkId!.Value).ToList();
        var linkCodes = await db.TrackedLinks.AsNoTracking().Where(l => linkIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code, ct);
        var sent = 0;
        foreach (var c in comments)
        {
            if (sent >= MaxRulesPerRun) break;
            var rule = rules.FirstOrDefault(r => c.At >= r.CreatedAt && Matches(c.Text, r.Keyword));
            if (rule is null) continue;
            var box = boxes.FirstOrDefault(b => b.Platform == c.Platform);
            if (box is null) continue;
            var text = rule.Message.Contains(LinkService.Placeholder, StringComparison.OrdinalIgnoreCase) && rule.LinkId is int lid && linkCodes.TryGetValue(lid, out var code)
                ? Regex.Replace(rule.Message, Regex.Escape(LinkService.Placeholder), links.Url(code), RegexOptions.IgnoreCase)
                : rule.Message;
            c.RuleId = rule.Id;
            try
            {
                await client.PrivateReplyAsync(box, c.Id, text, ct);
                c.PrivateReplyAt = DateTime.UtcNow;
                rule.Uses++;
                sent++;
            }
            catch (InstagramApiException ex)
            {
                // Np. minęło 7 dni albo osoba już dostała prywatną odpowiedź — nie ponawiamy.
                logger.LogInformation("Reguła {Keyword}: prywatna odpowiedź nieudana: {Message}", rule.Keyword, ex.Message);
            }
            await db.SaveChangesAsync(ct);
            if (c.PrivateReplyAt is not null && rule.PublicReply is { Length: > 0 } pub)
            {
                try { await ig.ReplyAsync(c.Id, pub); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogInformation(ex, "Reguła {Keyword}: odpowiedź pod komentarzem nieudana.", rule.Keyword); }
            }
            else if (c.PrivateReplyAt is not null)
            {
                await db.InstagramComments.Where(x => x.Id == c.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Done, true), ct);
            }
        }
        return sent;
    }

    // ── Porządki ─────────────────────────────────────────────────────

    public async Task CleanupAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        await db.SocialMessages.Where(m => m.At < cutoff).ExecuteDeleteAsync(ct);
        await db.SocialThreads.Where(t => t.LastMessageAt < cutoff).ExecuteDeleteAsync(ct);
    }

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}
