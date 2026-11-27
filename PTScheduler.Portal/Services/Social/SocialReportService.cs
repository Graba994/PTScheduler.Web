using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Cotygodniowy raport mediów społecznościowych (poniedziałek rano, e-mail do administratora): wyniki kanałów,
/// najlepszy post, kliknięcia i rejestracje z linków, co czeka w nadchodzącym tygodniu i 3 wnioski AI.
/// Bez AI raport też wychodzi (bez wniosków). Do AI idą tylko liczby i tytuły postów — bez danych osób.
/// </summary>
public class SocialReportService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    LinkService links,
    InstagramAi ai,
    EmailService email,
    IConfiguration config,
    ILogger<SocialReportService> logger)
{
    private static readonly CultureInfo Pl = new("pl-PL");

    public sealed record Channel(string Key, string Label, int Posts, int Interactions, int? Reach, int? Followers, int? FollowersDelta);
    public sealed record TopPost(string Platform, string Title, int Interactions, int? Reach, string? Permalink);
    public sealed record Data(
        DateOnly From, DateOnly To, List<Channel> Channels, TopPost? Top, int Comments, int OpenComments,
        int Clicks, int ClicksPrev, int Signups, int SignupsPrev, int Paid, int SignupsAll, int BioViews,
        LinkService.Row? TopLink, int ScheduledNext, int ToApprove, int ManualDue, int Failed);

    public async Task<bool> EnabledAsync() => await settings.GetAsync(K.SocialWeeklyOn) != "0";
    public Task SetEnabledAsync(bool on) => settings.SetAsync(K.SocialWeeklyOn, on ? "1" : "0");

    /// <summary>Tydzień ISO, np. „2026-W48” — raz na tydzień.</summary>
    public static string WeekKey(DateTime local) => $"{ISOWeek.GetYear(local)}-W{ISOWeek.GetWeekOfYear(local):00}";

    /// <summary>Z pętli w tle: w poniedziałek od 8:00 (czas polski), raz na tydzień.</summary>
    public async Task SendIfDueAsync(CancellationToken ct)
    {
        var local = InstagramRules.ToWarsaw(DateTime.UtcNow);
        if (local.DayOfWeek != DayOfWeek.Monday || local.Hour < 8) return;
        var week = WeekKey(local);
        if (await settings.GetAsync(K.SocialWeeklySent) == week) return;
        await settings.SetAsync(K.SocialWeeklySent, week);
        if (!await EnabledAsync()) return;
        var (ok, message) = await SendAsync(ct);
        if (!ok) logger.LogWarning("Raport tygodniowy mediów społecznościowych: {Message}", message);
    }

    public async Task<(bool Ok, string Message)> SendAsync(CancellationToken ct = default)
    {
        var to = await settings.GetAsync(K.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return (false, "Brak adresu e-mail powiadomień (Konfiguracja platformy → Powiadomienia).");
        var data = await BuildAsync(ct);
        var insights = await InsightsAsync(data, ct);
        var html = Html(data, insights, PortalUrls.Public(config));
        var (sent, error) = await email.SendAsync(to, Subject(data), html);
        return sent ? (true, $"Raport wysłany na {to}.") : (false, $"Nie udało się wysłać raportu: {error}");
    }

    public async Task<string> PreviewAsync(CancellationToken ct = default)
    {
        var data = await BuildAsync(ct);
        return Html(data, null, PortalUrls.Public(config));
    }

    public static string Subject(Data d) =>
        $"Media społecznościowe: tydzień {d.From:dd.MM}–{d.To:dd.MM} — {d.Signups} {Plural(d.Signups, "rejestracja", "rejestracje", "rejestracji")} z linków";

    // ── Dane ─────────────────────────────────────────────────────────

    public async Task<Data> BuildAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var since = nowUtc.AddDays(-7);
        var today = DateOnly.FromDateTime(InstagramRules.ToWarsaw(nowUtc));
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var media = await db.InstagramMedia.AsNoTracking().Where(m => m.PostedAt >= since && m.ProductType != "STORY").ToListAsync(ct);
        var titles = await db.InstagramPosts.AsNoTracking().Where(p => p.PublishedAt >= since.AddDays(-1))
            .Select(p => new { p.Id, p.Title }).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var days = await db.InstagramDaily.AsNoTracking().Where(d => d.Day >= today.AddDays(-8)).OrderBy(d => d.Day).ToListAsync(ct);
        var account = await db.InstagramAccounts.AsNoTracking().FirstOrDefaultAsync(ct);
        var channels = await db.SocialChannels.AsNoTracking().Where(c => c.Enabled).ToListAsync(ct);
        var targets = await db.SocialPostTargets.AsNoTracking().Include(t => t.Channel)
            .Where(t => t.Status == SocialTargetStatus.Published && t.PublishedAt >= since).ToListAsync(ct);
        var igPosts = await db.InstagramPosts.CountAsync(p => p.ToInstagram && p.Status == InstagramPostStatus.Published && p.PublishedAt >= since, ct);

        var list = new List<Channel>();
        if (account is not null)
        {
            var mine = media.Where(m => m.Platform == "instagram").ToList();
            int? delta = days.Count > 1 ? days[^1].Followers - days[0].Followers : null;
            list.Add(new Channel("instagram", "Instagram", Math.Max(igPosts, mine.Count), mine.Sum(InstagramRules.Interactions),
                days.Count > 0 ? days.Where(d => d.Day > today.AddDays(-8)).Sum(d => d.Reach ?? 0) : null, account.Followers, delta));
        }
        foreach (var ch in channels.GroupBy(c => c.Platform))
        {
            var key = LinkService.KeyOf(ch.Key);
            var mine = media.Where(m => m.Platform == key).ToList();
            var posted = targets.Count(t => t.Channel!.Platform == ch.Key);
            list.Add(new Channel(key, SocialPlatforms.Get(ch.Key).Label, Math.Max(posted, mine.Count), mine.Sum(InstagramRules.Interactions),
                mine.Any(m => m.Reach is not null) ? mine.Sum(m => m.Reach ?? 0) : null, ch.Sum(c => c.Followers ?? 0) is var f && f > 0 ? f : null, null));
        }

        var best = media.OrderByDescending(InstagramRules.Interactions).FirstOrDefault();
        TopPost? top = best is null ? null : new TopPost(
            best.Platform switch { "facebook" => "Facebook", "threads" => "Threads", _ => "Instagram" },
            best.PostId is int pid && titles.TryGetValue(pid, out var t) && !string.IsNullOrWhiteSpace(t) ? t : InstagramGraphics.Clean(best.Caption, 80),
            InstagramRules.Interactions(best), best.Reach, best.Permalink);

        var comments = await db.InstagramComments.CountAsync(c => !c.IsOwn && c.At >= since, ct);
        var open = await db.InstagramComments.CountAsync(c => !c.IsOwn && !c.Done && !c.Hidden, ct);

        var week = await links.ReportAsync(7);
        var two = await links.ReportAsync(14);
        var topLink = week.Rows.Where(r => r.Clicks > 0 || r.Signups > 0).OrderByDescending(r => r.Signups).ThenByDescending(r => r.Clicks).FirstOrDefault();

        var next = nowUtc.AddDays(7);
        var scheduled = await db.InstagramPosts.CountAsync(p => p.Status == InstagramPostStatus.Scheduled && p.ScheduledAt <= next, ct);
        var toApprove = await db.InstagramPosts.CountAsync(p => p.Status == InstagramPostStatus.ToApprove, ct);
        var manual = await db.SocialPostTargets.CountAsync(t => t.Status == SocialTargetStatus.ManualDue, ct);
        var failed = await db.SocialPostTargets.CountAsync(t => t.Status == SocialTargetStatus.Failed, ct)
                     + await db.InstagramPosts.CountAsync(p => p.Status == InstagramPostStatus.Failed, ct);

        return new Data(today.AddDays(-7), today.AddDays(-1), list, top, comments, open,
            week.Clicks, two.Clicks - week.Clicks, week.Signups, two.Signups - week.Signups, week.Paid, week.SignupsTotal, week.BioViews,
            topLink, scheduled, toApprove, manual, failed);
    }

    /// <summary>Same liczby dla AI (bez danych osób) — do wniosków.</summary>
    public static string Facts(Data d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Tydzień {d.From:dd.MM}–{d.To:dd.MM}.");
        foreach (var c in d.Channels)
            sb.AppendLine($"- {c.Label}: {c.Posts} postów, {c.Interactions} interakcji, zasięg {c.Reach?.ToString() ?? "?"}, obserwujący {c.Followers?.ToString() ?? "?"}{(c.FollowersDelta is int x ? $" ({x:+#;-#;0})" : "")}");
        if (d.Top is { } t) sb.AppendLine($"Najlepszy post: {t.Platform}, „{t.Title}”, {t.Interactions} interakcji, zasięg {t.Reach?.ToString() ?? "?"}.");
        sb.AppendLine($"Linki: {d.Clicks} kliknięć (tydzień wcześniej {d.ClicksPrev}), {d.Signups} rejestracji z linków (wcześniej {d.SignupsPrev}), {d.Paid} płatnych; wszystkich rejestracji {d.SignupsAll}; wejścia na stronę link w bio {d.BioViews}.");
        if (d.TopLink is { } l) sb.AppendLine($"Najlepszy link: {LinkService.ChannelLabel(l.Link.Channel)} — „{l.PostTitle ?? l.Link.Label}”: {l.Clicks} kliknięć, {l.Signups} rejestracji.");
        sb.AppendLine($"Komentarze: {d.Comments} nowych, {d.OpenComments} bez odpowiedzi. Następny tydzień: {d.ScheduledNext} zaplanowanych, {d.ToApprove} do akceptacji.");
        return sb.ToString();
    }

    private async Task<List<string>?> InsightsAsync(Data d, CancellationToken ct)
    {
        if (d.Channels.Count == 0 && d.Clicks == 0) return null;
        try
        {
            var (text, _) = await ai.WeeklyAsync(Facts(d), ct);
            return text?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().TrimStart('-', '*', '•', ' ').Trim())
                .Where(l => l.Length > 0).Take(3).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Raport tygodniowy: wnioski AI niedostępne.");
            return null;
        }
    }

    // ── E-mail ───────────────────────────────────────────────────────

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string N(int v) => v.ToString("N0", Pl);
    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && (n % 100 < 12 || n % 100 > 14) ? few : many;

    private static string Trend(int now, int prev)
    {
        if (prev == 0 && now == 0) return "";
        if (prev == 0) return " <span style=\"color:#059669\">(nowe)</span>";
        var pct = (int)Math.Round(100.0 * (now - prev) / prev);
        var color = pct >= 0 ? "#059669" : "#dc2626";
        return $" <span style=\"color:{color}\">({pct:+#;-#;0}%)</span>";
    }

    public static string Html(Data d, List<string>? insights, string portalUrl)
    {
        const string cell = "padding:8px 10px;border-bottom:1px solid #ece9f4;font-size:14px";
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:'Plus Jakarta Sans',Segoe UI,Arial,sans-serif;color:#17132a;max-width:640px\">");
        sb.Append($"<h2 style=\"font-size:20px;margin:0 0 4px\">Media społecznościowe — tydzień {d.From:dd.MM}–{d.To:dd.MM}</h2>");
        sb.Append("<p style=\"color:#6b6585;margin:0 0 16px\">Podsumowanie z Portalu Hantly.</p>");

        // Najważniejsze: z linków do rejestracji.
        sb.Append("<table role=\"presentation\" style=\"width:100%;border-collapse:separate;border-spacing:8px 0;margin:0 -8px 16px\"><tr>");
        foreach (var (label, value, extra) in new[]
                 {
                     ("Kliknięcia w linki", N(d.Clicks), Trend(d.Clicks, d.ClicksPrev)),
                     ("Rejestracje z linków", N(d.Signups), Trend(d.Signups, d.SignupsPrev)),
                     ("Płatne konta", N(d.Paid), ""),
                     ("Wejścia na link w bio", N(d.BioViews), "")
                 })
            sb.Append($"<td style=\"background:#f6f4fb;border-radius:12px;padding:12px;vertical-align:top\"><div style=\"font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:#6b6585\">{label}</div><div style=\"font-size:22px;font-weight:700;margin-top:4px\">{value}{extra}</div></td>");
        sb.Append("</tr></table>");
        sb.Append($"<p style=\"font-size:13px;color:#6b6585;margin:-8px 0 16px\">Wszystkich rejestracji w tygodniu: {N(d.SignupsAll)}.</p>");

        if (insights is { Count: > 0 })
        {
            sb.Append("<div style=\"background:#f3ecff;border-radius:12px;padding:12px 14px;margin-bottom:16px\"><b>Wnioski AI</b><ul style=\"margin:6px 0 0;padding-left:18px\">");
            foreach (var i in insights) sb.Append($"<li style=\"margin:4px 0\">{E(i)}</li>");
            sb.Append("</ul><div style=\"font-size:12px;color:#6b6585;margin-top:6px\">To podpowiedzi — decyzja należy do Ciebie.</div></div>");
        }

        if (d.Channels.Count > 0)
        {
            sb.Append("<h3 style=\"font-size:16px;margin:16px 0 6px\">Kanały</h3><table style=\"width:100%;border-collapse:collapse\">");
            sb.Append($"<tr style=\"text-align:left;color:#6b6585;font-size:12px\"><th style=\"{cell}\">Kanał</th><th style=\"{cell}\">Posty</th><th style=\"{cell}\">Interakcje</th><th style=\"{cell}\">Zasięg</th><th style=\"{cell}\">Obserwujący</th></tr>");
            foreach (var c in d.Channels)
                sb.Append($"<tr><td style=\"{cell}\"><b>{E(c.Label)}</b></td><td style=\"{cell}\">{c.Posts}</td><td style=\"{cell}\">{N(c.Interactions)}</td><td style=\"{cell}\">{(c.Reach is int r ? N(r) : "—")}</td><td style=\"{cell}\">{(c.Followers is int f ? N(f) : "—")}{(c.FollowersDelta is int x ? $" ({x:+#;-#;0})" : "")}</td></tr>");
            sb.Append("</table>");
        }

        if (d.Top is { } t)
        {
            sb.Append("<h3 style=\"font-size:16px;margin:16px 0 6px\">Najlepszy post</h3>");
            sb.Append($"<p style=\"margin:0\">{E(t.Platform)}: <b>{E(t.Title)}</b> — {N(t.Interactions)} interakcji{(t.Reach is int r ? $", zasięg {N(r)}" : "")}{(t.Permalink is not null ? $" · <a href=\"{E(t.Permalink)}\">zobacz</a>" : "")}</p>");
        }
        if (d.TopLink is { } l)
        {
            sb.Append("<h3 style=\"font-size:16px;margin:16px 0 6px\">Najlepszy link</h3>");
            sb.Append($"<p style=\"margin:0\">{E(LinkService.ChannelLabel(l.Link.Channel))}: <b>{E(string.IsNullOrWhiteSpace(l.PostTitle) ? l.Link.Label : l.PostTitle)}</b> — {N(l.Clicks)} kliknięć, {N(l.Signups)} {Plural(l.Signups, "rejestracja", "rejestracje", "rejestracji")}</p>");
        }

        sb.Append("<h3 style=\"font-size:16px;margin:16px 0 6px\">Do zrobienia</h3><ul style=\"margin:0;padding-left:18px\">");
        sb.Append($"<li>Zaplanowane na najbliższe 7 dni: <b>{d.ScheduledNext}</b>{(d.ScheduledNext == 0 ? " — warto przygotować posty (Autopilot albo Pomysły)" : "")}</li>");
        if (d.ToApprove > 0) sb.Append($"<li>Czeka na akceptację: <b>{d.ToApprove}</b></li>");
        if (d.ManualDue > 0) sb.Append($"<li>Do wrzucenia ręcznie: <b>{d.ManualDue}</b></li>");
        if (d.Failed > 0) sb.Append($"<li style=\"color:#dc2626\">Nieudane publikacje: <b>{d.Failed}</b></li>");
        sb.Append($"<li>Komentarze: {N(d.Comments)} nowych, <b>{N(d.OpenComments)}</b> bez odpowiedzi</li></ul>");

        sb.Append($"<p style=\"margin:20px 0 0\"><a href=\"{E(portalUrl + "/panel/social")}\" style=\"display:inline-block;background:#7c3aed;color:#fff;text-decoration:none;padding:10px 16px;border-radius:10px;font-weight:600\">Otwórz media społecznościowe</a></p>");
        sb.Append("<p style=\"font-size:12px;color:#6b6585;margin-top:16px\">Raport wychodzi w poniedziałki rano. Wyłączysz go w Portalu: Media społecznościowe → Linki i wyniki.</p></div>");
        return sb.ToString();
    }
}
