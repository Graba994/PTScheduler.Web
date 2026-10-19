using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Poranny raport Leo dla administratora platformy: wczoraj w liczbach, sprawy do zrobienia,
/// trenerzy zagrożeni odejściem, powody do radości. E-mail z grafiką agenta (PNG — zmiana ikon w Panelu
/// zmienia też szablon). Liczby z reguł, AI pisze najwyżej wstęp.
/// </summary>
public class LeoReportService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    LeoService leo,
    AssistantService assistant,
    AiService ai,
    EmailService email,
    IConfiguration config,
    IWebHostEnvironment env)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public sealed record Yesterday(int Registrations, int Launched, decimal Payments, int Feedback, int Surveys, int Meetings, int ActiveTenants, decimal AiUsd);

    public sealed record Report(DateTime LocalDay, string Intro, Yesterday Stats, List<LeoService.Tip> Todo, List<LeoService.Tip> Praise,
        List<LeoService.Tip> Review, List<LeoService.Risk> Risks, AssistantService.Config Leo);

    public async Task<Report> BuildAsync()
    {
        var local = BillingService.LocalNow;
        var tips = await leo.GetTipsAsync();
        var risks = (await leo.GetRisksAsync()).Take(5).ToList();
        var stats = await YesterdayAsync(local);
        var cfg = await assistant.GetLeoConfigAsync();

        var todo = tips.Where(t => t.IsTodo).ToList();
        var praise = tips.Where(t => t.IsPraise).ToList();
        var review = tips.Where(t => t.Mood == "think").ToList();

        var fallback = new StringBuilder($"Wczoraj: {stats.Registrations} {LeoService.Plural(stats.Registrations, "rejestracja", "rejestracje", "rejestracji")}");
        if (stats.Payments > 0) fallback.Append($", wpłaty {stats.Payments.ToString("#,0.##", Pl)} zł");
        fallback.Append(todo.Count > 0 ? $". Do zrobienia: {todo.Count}." : ". Nic pilnego — spokojny poranek.");
        var facts = new Dictionary<string, string>
        {
            ["rejestracje_wczoraj"] = stats.Registrations.ToString(),
            ["wplaty_wczoraj_zl"] = stats.Payments.ToString("#,0.##", Pl),
            ["sprawy_do_zrobienia"] = todo.Count.ToString(),
            ["zagrozeni_trenerzy"] = risks.Count(r => r.High).ToString(),
            ["aktywni_trenerzy"] = stats.ActiveTenants.ToString(),
        };
        var intro = cfg.Ai
            ? (await assistant.PhraseAsync(null, "poranny_raport", facts, fallback.ToString(), audience: "admin")).Text
            : fallback.ToString();
        return new(local, intro, stats, todo, praise, review, risks, cfg);
    }

    private async Task<Yesterday> YesterdayAsync(DateTime local)
    {
        // Doba lokalna (Warszawa) przeliczona na UTC.
        var offset = TimeSpan.FromMinutes(Math.Round((local - DateTime.UtcNow).TotalMinutes));
        var fromUtc = DateTime.SpecifyKind(local.Date.AddDays(-1) - offset, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(local.Date - offset, DateTimeKind.Utc);
        await using var db = await dbFactory.CreateDbContextAsync();
        var registrations = await db.Tenants.CountAsync(t => t.CreatedAt >= fromUtc && t.CreatedAt < toUtc);
        var launched = await db.TenantEvents.CountAsync(e => (e.EventType == TenantEventTypes.Provisioned || e.EventType == TenantEventTypes.AutoLaunched)
            && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc);
        // Wszystkie wpłaty (rachunki, sklep, Stripe) trafiają do PaymentRecords.
        var payments = await db.PaymentRecords.Where(p => p.Status == PaymentRecordStatus.Paid && p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var feedback = await db.AppFeedbacks.CountAsync(f => f.CreatedAt >= fromUtc && f.CreatedAt < toUtc);
        var surveys = await db.TrainerSurveyResponses.CountAsync(r => r.CreatedAt >= fromUtc && r.CreatedAt < toUtc);
        var meetings = await db.TrainerMeetings.CountAsync(m => m.CreatedAt >= fromUtc && m.CreatedAt < toUtc);
        var active = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active);
        var aiUsd = await db.AiUsageLogs.Where(l => l.CreatedAt >= fromUtc && l.CreatedAt < toUtc).SumAsync(l => (decimal?)l.CostUsd) ?? 0m;
        return new(registrations, launched, payments, feedback, surveys, meetings, active, aiUsd);
    }

    public async Task<string?> RecipientAsync()
    {
        var s = await settings.GetAllAsync(K.AdminNotificationEmail, K.ErrorAlertEmail);
        return !string.IsNullOrWhiteSpace(s[K.AdminNotificationEmail]) ? s[K.AdminNotificationEmail].Trim()
            : !string.IsNullOrWhiteSpace(s[K.ErrorAlertEmail]) ? s[K.ErrorAlertEmail].Trim() : null;
    }

    /// <summary>Wysyła raport (przy <paramref name="force"/> także przy wyłączonym raporcie). Zwraca błąd albo null.</summary>
    /// <param name="baseUrl">Adres Portalu do linków i obrazków, gdy nie ustawiono Portal:PublicUrl (próbna wysyłka z Panelu).</param>
    public async Task<string?> SendAsync(bool force = false, string? baseUrl = null)
    {
        if (!force && await settings.GetAsync(K.LeoReportEnabled) == "false") return "Raport wyłączony.";
        var to = await RecipientAsync();
        if (to is null) return "Brak adresu administratora — wpisz go w ustawieniach raportu.";
        var root = (config.GetValue<string>("Portal:PublicUrl") is { Length: > 0 } pub ? pub : baseUrl ?? "https://hantly.pl").TrimEnd('/');
        var report = await BuildAsync();
        var version = await assistant.IconsVersionAsync(AssistantService.Leo);
        // Grafika Leo w środku wiadomości (cid:) — widać ją od razu, także bez publicznego adresu Portalu.
        var png = await assistant.EmailImageAsync(Mood(report), env.WebRootPath, AssistantService.Leo);
        var html = RenderHtml(report, root, version, png is null ? null : "cid:leo");
        var subject = report.Todo.Count > 0
            ? $"{report.Leo.Name}: {report.Todo.Count} {LeoService.Plural(report.Todo.Count, "sprawa", "sprawy", "spraw")} na dziś"
            : $"{report.Leo.Name}: spokojny poranek";
        var (ok, error) = await email.SendAsync(to, subject, html, fromNameOverride: $"{report.Leo.Name} · Panel",
            images: png is { } p ? [new EmailService.InlineImage("leo", p.Data, p.ContentType)] : null);
        return ok ? null : error ?? "Nie udało się wysłać e-maila.";
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Text(LeoService.Tip t)
    {
        var text = t.Fallback;
        foreach (var (k, v) in t.Placeholders ?? []) text = text.Replace(k, v);
        return text;
    }

    public static string Mood(Report r) => r.Todo.Count > 0 ? "remind" : r.Praise.Count > 0 ? "celebrate" : "hello";

    /// <param name="imgSrc">Adres grafiki Leo (np. <c>cid:leo</c> przy obrazku w wiadomości); bez niego — PNG z Portalu.</param>
    public static string RenderHtml(Report r, string root, string version, string? imgSrc = null)
    {
        var img = imgSrc ?? $"{root}/assistant/leo/{Mood(r)}.png?v={version}";
        var day = r.LocalDay.ToString("dddd, d MMMM", Pl);
        var sb = new StringBuilder();
        sb.Append("""<!doctype html><html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>""");
        sb.Append("""<body style="margin:0;padding:24px 12px;background:#F6F1E7;font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;color:#1F2937">""");
        sb.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;margin:0 auto;background:#fff;border-radius:20px;overflow:hidden;box-shadow:0 10px 30px rgba(31,42,68,.08)">""");

        // Nagłówek z Leo.
        sb.Append("""<tr><td style="background:#1F2A44;padding:22px 26px"><table role="presentation" cellpadding="0" cellspacing="0" width="100%"><tr>""");
        sb.Append($"""<td width="84" valign="middle"><img src="{E(img)}" width="76" height="76" alt="{E(r.Leo.Name)}" style="display:block;border:0"></td>""");
        sb.Append($"""<td valign="middle" style="padding-left:12px"><div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#F7C670;font-weight:700">{E(r.Leo.Name)} · {E(day)}</div>""");
        sb.Append("""<div style="font-size:22px;font-weight:700;color:#fff;margin-top:4px">Dzień dobry! Oto poranny raport</div></td></tr></table></td></tr>""");

        sb.Append("""<tr><td style="padding:22px 26px 6px">""");
        sb.Append($"""<div style="background:#FFF7E8;border:1px solid #F5DDB0;border-radius:14px;padding:12px 16px;font-size:15px;line-height:1.5">{E(r.Intro)}</div>""");

        // Wczoraj w liczbach.
        sb.Append(Heading("Wczoraj w liczbach", "#B45309"));
        sb.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>""");
        foreach (var (value, label) in new[]
        {
            (r.Stats.Registrations.ToString(), "rejestracje"),
            (r.Stats.Launched.ToString(), "uruchomione"),
            (r.Stats.Payments.ToString("#,0.##", Pl) + " zł", "wpłaty"),
            (r.Stats.Feedback.ToString(), "oceny"),
            (r.Stats.Surveys.ToString(), "ankiety"),
        })
            sb.Append($"""<td width="20%" style="padding:4px"><div style="border:1px solid #EDE7DA;border-radius:12px;padding:10px 6px;text-align:center"><div style="font-size:18px;font-weight:700;color:#1F2A44">{E(value)}</div><div style="font-size:11px;color:#6B7280">{label}</div></div></td>""");
        sb.Append("</tr></table>");
        sb.Append($"""<div style="font-size:12px;color:#6B7280;margin-top:6px">Aktywni trenerzy: {r.Stats.ActiveTenants}{(r.Stats.Meetings > 0 ? $" · spotkania: {r.Stats.Meetings}" : "")}{(r.Stats.AiUsd > 0 ? $" · AI: {r.Stats.AiUsd.ToString("0.00", Pl)} $" : "")}</div>""");

        if (r.Todo.Count > 0) sb.Append(TipList("Do zrobienia", "#B91C1C", "📌", r.Todo, root));

        if (r.Risks.Count > 0)
        {
            sb.Append(Heading("Ryzyko odejścia", "#B91C1C"));
            foreach (var k in r.Risks)
            {
                var color = k.High ? "#B91C1C" : "#B45309";
                sb.Append($"""<div style="border:1px solid #F1E4E4;border-radius:12px;padding:10px 14px;margin-bottom:8px">""");
                sb.Append($"""<div style="font-weight:700;font-size:15px">{E(k.Name)} <span style="display:inline-block;margin-left:6px;padding:1px 8px;border-radius:999px;background:{color};color:#fff;font-size:11px;font-weight:700">{k.Score} pkt · {k.Level}</span></div>""");
                sb.Append($"""<div style="font-size:13px;color:#4B5563;margin-top:3px">{E(string.Join(" · ", k.Reasons.Take(3)))}</div>""");
                sb.Append($"""<div style="font-size:13px;margin-top:4px">💡 {E(k.Suggestion)}</div></div>""");
            }
            sb.Append($"""<a href="{E(root)}/panel/ryzyko" style="font-size:13px;color:#1F2A44;font-weight:700">Pełna lista z kontaktami →</a>""");
        }

        if (r.Praise.Count > 0) sb.Append(TipList("Powody do radości", "#047857", "🎉", r.Praise, root));
        if (r.Review.Count > 0) sb.Append(TipList("Do przejrzenia", "#4338CA", "🔎", r.Review, root));

        sb.Append($"""<div style="padding:22px 0 8px"><a href="{E(root)}/panel" style="display:inline-block;background:#1F2A44;color:#fff;text-decoration:none;font-weight:700;padding:12px 22px;border-radius:12px">Otwórz Panel</a></div>""");
        sb.Append("</td></tr>");
        sb.Append($"""<tr><td style="padding:12px 26px 22px;font-size:12px;color:#9CA3AF">Poranny raport od {E(r.Leo.Name)}. Godzinę zmienisz albo raport wyłączysz w <a href="{E(root)}/panel/asystent/leo" style="color:#6B7280">Panel → Asystent i AI → {E(r.Leo.Name)}</a>.</td></tr>""");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string Heading(string text, string color) =>
        $"""<div style="margin:22px 0 10px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;font-weight:700;color:{color}">{E(text)}</div>""";

    private static string TipList(string title, string color, string icon, List<LeoService.Tip> tips, string root)
    {
        var sb = new StringBuilder(Heading(title, color));
        foreach (var t in tips)
        {
            sb.Append($"""<div style="font-size:14px;line-height:1.5;margin-bottom:8px">{icon} {E(Text(t))}""");
            if (t.ActionHref is not null) sb.Append($""" <a href="{E(root + t.ActionHref)}" style="color:#1F2A44;font-weight:700;text-decoration:none">{E(t.ActionLabel ?? "Zobacz")} →</a>""");
            sb.Append("</div>");
        }
        return sb.ToString();
    }
}

/// <summary>Co 10 minut sprawdza, czy minęła godzina porannego raportu (czas warszawski) i czy dziś już poszedł; w poniedziałek także tygodniowego.</summary>
public class LeoReportBackgroundService(IServiceScopeFactory scopes, ILogger<LeoReportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Poranny raport Leo się nie udał."); }
            await Task.Delay(TimeSpan.FromMinutes(10), ct);
        }
    }

    private async Task TickAsync()
    {
        var local = BillingService.LocalNow;
        if (local.Hour is < 5 or >= 12) return;
        using var scope = scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
        var s = await settings.GetAllAsync(K.LeoReportEnabled, K.LeoReportHour, K.LeoReportSentOn, K.LeoEnabled, K.LeoWeeklyEnabled, K.LeoWeeklySentOn);
        if (s[K.LeoEnabled] == "false") return;
        var hour = int.TryParse(s[K.LeoReportHour], out var h) ? Math.Clamp(h, 5, 11) : 7;
        var today = local.ToString("yyyy-MM-dd");
        if (local.Hour < hour) return;

        // Najpierw znacznik — przy błędzie wysyłki raport nie pójdzie dwa razy (spróbujemy jutro).
        if (s[K.LeoReportEnabled] != "false" && s[K.LeoReportSentOn] != today)
        {
            await settings.SetAsync(K.LeoReportSentOn, today);
            if (await scope.ServiceProvider.GetRequiredService<LeoReportService>().SendAsync() is { } error)
                logger.LogInformation("Poranny raport Leo nie wysłany: {Error}", error);
        }

        // W poniedziałek także raport tygodniowy (o tej samej godzinie).
        if (local.DayOfWeek == DayOfWeek.Monday && s[K.LeoWeeklyEnabled] != "false" && s[K.LeoWeeklySentOn] != today)
        {
            await settings.SetAsync(K.LeoWeeklySentOn, today);
            if (await scope.ServiceProvider.GetRequiredService<LeoWeeklyReport>().SendAsync() is { } error)
                logger.LogInformation("Tygodniowy raport Leo nie wysłany: {Error}", error);
        }
    }
}
