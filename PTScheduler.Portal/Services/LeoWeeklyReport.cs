using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Tygodniowy raport biznesowy Leo (poniedziałek rano i podgląd w „Leo — analiza”): tydzień w liczbach
/// z porównaniem do poprzedniego, ostrzeżenia, segmenty, pieniądze zagrożone i o co proszą trenerzy.
/// Liczby z reguł (<see cref="LeoAnalytics"/>), AI pisze najwyżej wstęp.
/// </summary>
public class LeoWeeklyReport(IDbContextFactory<PortalDbContext> dbFactory, LeoAnalytics analytics, LeoReportService daily,
    AssistantService assistant, EmailService email, SiteSettingsService settings, IConfiguration config)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public sealed record Pair(decimal Now, decimal Prev)
    {
        public string Delta => Prev == 0 ? (Now == 0 ? "bez zmian" : "nowe") : Now == Prev ? "bez zmian"
            : $"{(Now > Prev ? "+" : "−")}{Math.Round(Math.Abs(Now - Prev) * 100 / Prev)}%";
        public bool Up => Now > Prev;
    }

    public sealed record Weekly(DateTime From, DateTime To, string Intro,
        Pair Registrations, Pair Launched, Pair Payments, Pair Sessions, int Clients, int ClientsChange, int ActiveStudios,
        decimal TrainerRevenue30, LeoAnalytics.Overview Overview, AssistantService.Config Leo);

    public async Task<Weekly> BuildAsync()
    {
        var o = await analytics.BuildAsync();
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var twoWeeks = now.AddDays(-14);
        await using var db = await dbFactory.CreateDbContextAsync();

        async Task<Pair> CountAsync(Func<DateTime, DateTime, Task<decimal>> q) => new(await q(weekAgo, now), await q(twoWeeks, weekAgo));
        var registrations = await CountAsync(async (a, b) => await db.Tenants.CountAsync(t => t.CreatedAt >= a && t.CreatedAt < b));
        var launched = await CountAsync(async (a, b) => await db.TenantEvents.CountAsync(e =>
            (e.EventType == TenantEventTypes.Provisioned || e.EventType == TenantEventTypes.AutoLaunched) && e.OccurredAt >= a && e.OccurredAt < b));
        var payments = await CountAsync(async (a, b) => await db.PaymentRecords
            .Where(p => p.Status == PaymentRecordStatus.Paid && p.CreatedAt >= a && p.CreatedAt < b).SumAsync(p => (decimal?)p.Amount) ?? 0m);

        var withData = o.Studios.Where(s => s.Sessions7 is not null).ToList();
        var sessions = new Pair(withData.Sum(s => s.Sessions7 ?? 0), withData.Sum(s => s.Sessions7Prev ?? 0));
        var clients = o.Studios.Sum(s => s.Clients);
        var clientsChange = o.Studios.Where(s => s.Clients7Ago is not null).Sum(s => s.Clients - s.Clients7Ago!.Value);
        var revenue = o.Studios.Sum(s => s.Revenue30 ?? 0);
        var cfg = await assistant.GetLeoConfigAsync();

        var fallback = new StringBuilder($"Tydzień: {registrations.Now} {LeoService.Plural((int)registrations.Now, "rejestracja", "rejestracje", "rejestracji")}");
        if (payments.Now > 0) fallback.Append($", wpłaty {LeoAnalytics.Zl(payments.Now)}");
        if (withData.Count > 0) fallback.Append($", {sessions.Now} {LeoService.Plural((int)sessions.Now, "wizyta", "wizyty", "wizyt")} u trenerów ({sessions.Delta})");
        fallback.Append(o.Warnings.Count > 0 ? $". Ostrzeżeń: {o.Warnings.Count} — zacznij od nich." : ". Bez niepokojących zmian.");
        var facts = new Dictionary<string, string>
        {
            ["rejestracje_tydzien"] = registrations.Now.ToString("0"),
            ["wplaty_tydzien_zl"] = payments.Now.ToString("#,0.##", Pl),
            ["wizyty_u_trenerow"] = sessions.Now.ToString("0"),
            ["zmiana_wizyt"] = sessions.Delta,
            ["ostrzezenia"] = o.Warnings.Count.ToString(),
            ["zagrozone_zl_mies"] = o.Money.MonthlyAtRisk.ToString("#,0.##", Pl),
        };
        var intro = cfg.Ai
            ? (await assistant.PhraseAsync(null, "raport_tygodniowy", facts, fallback.ToString(), audience: "admin")).Text
            : fallback.ToString();
        var local = BillingService.LocalNow;
        return new(local.Date.AddDays(-7), local.Date.AddDays(-1), intro, registrations, launched, payments, sessions, clients, clientsChange,
            o.Studios.Count, revenue, o, cfg);
    }

    /// <summary>Wysyła raport na adres administratora. Zwraca błąd albo null.</summary>
    public async Task<string?> SendAsync(bool force = false, string? baseUrl = null)
    {
        if (!force && await settings.GetAsync(K.LeoWeeklyEnabled) == "false") return "Raport tygodniowy wyłączony.";
        var to = await daily.RecipientAsync();
        if (to is null) return "Brak adresu administratora — wpisz go w ustawieniach raportu.";
        var root = (config.GetValue<string>("Portal:PublicUrl") is { Length: > 0 } pub ? pub : baseUrl ?? "").TrimEnd('/');
        var report = await BuildAsync();
        var version = await assistant.IconsVersionAsync(AssistantService.Leo);
        var subject = $"{report.Leo.Name}: tydzień {report.From:dd.MM}–{report.To:dd.MM}"
            + (report.Overview.Warnings.Count > 0 ? $" · {report.Overview.Warnings.Count} {LeoService.Plural(report.Overview.Warnings.Count, "ostrzeżenie", "ostrzeżenia", "ostrzeżeń")}" : "");
        var (ok, error) = await email.SendAsync(to, subject, RenderHtml(report, root, version), fromNameOverride: $"{report.Leo.Name} · Panel");
        return ok ? null : error ?? "Nie udało się wysłać e-maila.";
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    public static string RenderHtml(Weekly r, string root, string version)
    {
        var o = r.Overview;
        var mood = o.Warnings.Any(w => w.Severity >= 3) ? "remind" : r.Sessions.Up || r.Registrations.Up ? "celebrate" : "hello";
        var img = $"{root}/assistant/leo/{mood}.png?v={version}";
        var sb = new StringBuilder();
        sb.Append("""<!doctype html><html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>""");
        sb.Append("""<body style="margin:0;padding:24px 12px;background:#F6F1E7;font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;color:#1F2937">""");
        sb.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;margin:0 auto;background:#fff;border-radius:20px;overflow:hidden;box-shadow:0 10px 30px rgba(31,42,68,.08)">""");
        sb.Append("""<tr><td style="background:#1F2A44;padding:22px 26px"><table role="presentation" cellpadding="0" cellspacing="0" width="100%"><tr>""");
        sb.Append($"""<td width="84" valign="middle"><img src="{E(img)}" width="76" height="76" alt="{E(r.Leo.Name)}" style="display:block;border:0"></td>""");
        sb.Append($"""<td valign="middle" style="padding-left:12px"><div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#F7C670;font-weight:700">{E(r.Leo.Name)} · {r.From:dd.MM}–{r.To:dd.MM.yyyy}</div>""");
        sb.Append("""<div style="font-size:22px;font-weight:700;color:#fff;margin-top:4px">Raport tygodniowy</div></td></tr></table></td></tr>""");

        sb.Append("""<tr><td style="padding:22px 26px 6px">""");
        sb.Append($"""<div style="background:#FFF7E8;border:1px solid #F5DDB0;border-radius:14px;padding:12px 16px;font-size:15px;line-height:1.5">{E(r.Intro)}</div>""");

        sb.Append(Heading("Tydzień w liczbach", "#B45309"));
        sb.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>""");
        foreach (var (value, label, delta) in new[]
        {
            (r.Registrations.Now.ToString("0"), "rejestracje", r.Registrations.Delta),
            (r.Launched.Now.ToString("0"), "uruchomione", r.Launched.Delta),
            (LeoAnalytics.Zl(r.Payments.Now), "wpłaty", r.Payments.Delta),
            (r.Sessions.Now.ToString("0"), "wizyty u trenerów", r.Sessions.Delta),
        })
            sb.Append($"""<td width="25%" style="padding:4px"><div style="border:1px solid #EDE7DA;border-radius:12px;padding:10px 6px;text-align:center"><div style="font-size:18px;font-weight:700;color:#1F2A44">{E(value)}</div><div style="font-size:11px;color:#6B7280">{label}</div><div style="font-size:11px;color:#9CA3AF">{E(delta)}</div></div></td>""");
        sb.Append("</tr></table>");
        sb.Append($"""<div style="font-size:12px;color:#6B7280;margin-top:6px">Aktywne studia: {r.ActiveStudios} · klienci u trenerów: {r.Clients}{(r.ClientsChange != 0 ? $" ({(r.ClientsChange > 0 ? "+" : "")}{r.ClientsChange})" : "")} · wpłaty klientów w aplikacjach (30 dni): {E(LeoAnalytics.Zl(r.TrainerRevenue30))}</div>""");

        if (o.Warnings.Count > 0)
        {
            sb.Append(Heading("Ostrzeżenia", "#B91C1C"));
            foreach (var g in o.Warnings.GroupBy(w => w.Studio.Id).OrderByDescending(g => g.Max(w => w.Severity)).Take(5))
            {
                var list = g.ToList();
                sb.Append($"""<div style="font-size:14px;line-height:1.5;margin-bottom:10px">⚠️ <b>{E(list[0].Studio.Name)}</b>: {E(string.Join(" · ", list.Select(w => w.Title)))}<br><span style="font-size:13px;color:#4B5563">{E(string.Join(" ", list.Take(2).Select(w => w.Detail)))}</span><br><span style="font-size:13px">💡 {E(list[0].Action)}</span></div>""");
            }
        }

        if (o.Money.Items.Count > 0)
        {
            sb.Append(Heading("Pieniądze zagrożone", "#B91C1C"));
            sb.Append($"""<div style="font-size:14px;margin-bottom:8px">Abonamenty z ryzykiem odejścia: <b>{E(LeoAnalytics.Zl(o.Money.MonthlyAtRisk))}</b> miesięcznie ({E(LeoAnalytics.Zl(o.Money.YearlyAtRisk))} rocznie), oczekiwana strata: {E(LeoAnalytics.Zl(o.Money.ExpectedMonthly))}/mies.</div>""");
            foreach (var i in o.Money.Items.Take(3))
                sb.Append($"""<div style="font-size:13px;color:#4B5563;margin-bottom:4px">• {E(i.Studio.Name)} — {E(LeoAnalytics.Zl(i.Monthly))}/mies., ryzyko {i.Score}%</div>""");
        }

        sb.Append(Heading("Segmenty", "#4338CA"));
        foreach (var seg in o.Segments)
        {
            var names = seg.Items.Count == 0 ? "nikt" : string.Join(", ", seg.Items.Take(4).Select(i => i.Studio.Name)) + (seg.Items.Count > 4 ? $" i {seg.Items.Count - 4} więcej" : "");
            sb.Append($"""<div style="font-size:14px;margin-bottom:6px"><b>{E(seg.Title)}</b> ({seg.Items.Count}): {E(names)}</div>""");
        }

        if (o.Voice.Topics.Count > 0)
        {
            sb.Append(Heading("O co proszą trenerzy", "#047857"));
            foreach (var t in o.Voice.Topics.Take(3))
                sb.Append($"""<div style="font-size:14px;margin-bottom:6px">🗣️ {E(t.Label)} — {t.Mentions} {LeoService.Plural(t.Mentions, "wzmianka", "wzmianki", "wzmianek")}</div>""");
        }

        sb.Append($"""<div style="padding:22px 0 8px"><a href="{E(root)}/panel/leo" style="display:inline-block;background:#1F2A44;color:#fff;text-decoration:none;font-weight:700;padding:12px 22px;border-radius:12px">Otwórz analizę</a></div>""");
        sb.Append("</td></tr>");
        sb.Append($"""<tr><td style="padding:12px 26px 22px;font-size:12px;color:#9CA3AF">Raport tygodniowy od {E(r.Leo.Name)} — w każdy poniedziałek o godzinie porannego raportu. Wyłączysz go w <a href="{E(root)}/panel/leo" style="color:#6B7280">Panel → Leo — analiza</a>.</td></tr>""");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string Heading(string text, string color) =>
        $"""<div style="margin:22px 0 10px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;font-weight:700;color:{color}">{E(text)}</div>""";
}
