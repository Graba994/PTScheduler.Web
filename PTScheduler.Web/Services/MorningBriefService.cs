using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Poranny brief trenera od asystenta-maskotki: plan dnia (z oznaczeniami: 10. trening, pierwsza wizyta,
/// płatność u trenera, ostatnie wejście w pakiecie, uwagi w ankiecie zdrowotnej), sprawy do załatwienia
/// i pochwały. E-mail z obrazkiem maskotki z Portalu (zmiana wyglądu w Panelu zmienia też e-mail) i krótki push.
/// Bez treningów i bez spraw do załatwienia — nic nie wysyłamy.
/// </summary>
public class MorningBriefService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    AssistantTipsService tips,
    PlatformAssistantService assistant,
    IEmailService email,
    IWebPushService push,
    INotificationPreferencesService prefs,
    IBrandingService branding,
    IAppClock clock,
    ILogger<MorningBriefService> logger)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public sealed record SessionLine(DateTime Start, string Client, string Type, List<string> Flags);
    public sealed record Brief(string FirstName, DateOnly Day, List<SessionLine> Sessions, List<AssistantTipsService.Tip> Todo,
        List<AssistantTipsService.Tip> Praise, string Intro);

    /// <summary>Dane briefu albo null, gdy dziś nie ma o czym pisać.</summary>
    public async Task<Brief?> BuildAsync(ApplicationUser user, bool studioWide, bool force = false)
    {
        var now = clock.LocalNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);
        await using var db = await dbFactory.CreateDbContextAsync();

        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => (studioWide || s.TrainerUserId == user.Id) && s.Status == SessionStatus.Scheduled && s.StartTime >= today && s.StartTime < tomorrow)
            .OrderBy(s => s.StartTime)
            .Select(s => new
            {
                s.StartTime, s.ClientId, s.OffPackagePayment, s.PaidAt,
                Client = s.Client.FirstName + " " + s.Client.LastName,
                Type = s.SessionType.Name,
                PackageLeft = s.Package != null ? (int?)(s.Package.TotalSessions - s.Package.UsedSessions) : null
            })
            .ToListAsync();

        var clientIds = sessions.Select(s => s.ClientId).Distinct().ToList();
        var completed = await db.Sessions.AsNoTracking()
            .Where(s => clientIds.Contains(s.ClientId) && s.Status == SessionStatus.Completed)
            .GroupBy(s => s.ClientId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        var healthFlags = await db.SurveyResponses.AsNoTracking()
            .Where(r => clientIds.Contains(r.ClientId) && r.Kind == SurveyKind.HealthIntake)
            .GroupBy(r => r.ClientId)
            .Select(g => new { g.Key, Flags = g.OrderByDescending(r => r.SubmittedAt).First().FlagCount })
            .ToDictionaryAsync(g => g.Key, g => g.Flags);

        var lines = new List<SessionLine>();
        var nth = new Dictionary<int, int>();
        foreach (var s in sessions)
        {
            var flags = new List<string>();
            var done = completed.GetValueOrDefault(s.ClientId);
            var number = done + 1 + nth.GetValueOrDefault(s.ClientId);
            nth[s.ClientId] = nth.GetValueOrDefault(s.ClientId) + 1;
            if (number == 1) flags.Add("pierwszy trening");
            else if (number % 10 == 0) flags.Add($"{number}. trening 🎉");
            if (s.OffPackagePayment == "trainer" && s.PaidAt is null) flags.Add("płatność u Ciebie");
            if (s.PackageLeft is 1) flags.Add("ostatnie wejście w pakiecie");
            if (healthFlags.GetValueOrDefault(s.ClientId) > 0) flags.Add("uwagi w ankiecie zdrowotnej");
            lines.Add(new(s.StartTime, s.Client.Trim(), s.Type, flags));
        }

        var all = await tips.GetTipsAsync(user.Id, studioWide);
        var todo = all.Where(t => t.Kind is "akceptacje" or "niezaplacone" or "faktury" or "pakiety" or "nieaktywni").ToList();
        var praise = all.Where(t => t.Kind is "kamien_milowy" or "rekord_tygodnia").ToList();
        if (!force && lines.Count == 0 && todo.Count == 0) return null;

        var firstName = string.IsNullOrWhiteSpace(user.FirstName) ? "" : user.FirstName.Trim();
        var fallback = lines.Count == 0
            ? "Dziś bez treningów w grafiku — dobry moment na sprawy poniżej."
            : $"Dziś {lines.Count} {AssistantTipsService.Plural(lines.Count, "trening", "treningi", "treningów")}, pierwszy o {lines[0].Start:HH:mm}." +
              (todo.Count > 0 ? $" Do załatwienia: {todo.Count}." : " Wszystkie sprawy ogarnięte.");
        var config = await assistant.GetConfigAsync();
        var facts = new Dictionary<string, string>
        {
            ["treningi_dzis"] = lines.Count.ToString(),
            ["pierwszy_o"] = lines.Count > 0 ? lines[0].Start.ToString("HH:mm") : "—",
            ["sprawy_do_zalatwienia"] = todo.Count.ToString(),
            ["pochwaly"] = praise.Count.ToString(),
        };
        var intro = await assistant.PhraseAsync($"brief-{user.Id}-{today:yyyyMMdd}", "poranny_brief", facts, fallback, config?.Ai == true);
        return new Brief(firstName, DateOnly.FromDateTime(today), lines, todo, praise, intro);
    }

    /// <summary>Wysyła brief jednej osobie (e-mail i/lub push wg jej ustawień). Zwraca, czy coś poszło.</summary>
    public async Task<bool> SendAsync(ApplicationUser user, bool studioWide, bool force = false)
    {
        var p = await prefs.GetAsync(user.Id);
        var brief = await BuildAsync(user, studioWide, force);
        if (brief is null) return false;
        var config = await assistant.GetConfigAsync();
        var sent = false;

        if ((p.MorningBrief || force) && !string.IsNullOrWhiteSpace(user.Email) && await email.IsEnabledAsync())
        {
            var studio = (await branding.GetAsync()).CompanyName;
            var subject = brief.Sessions.Count > 0
                ? $"Twój dzień: {brief.Sessions.Count} {AssistantTipsService.Plural(brief.Sessions.Count, "trening", "treningi", "treningów")}, pierwszy o {brief.Sessions[0].Start:HH:mm}"
                : "Twój dzień: sprawy do załatwienia";
            try
            {
                await email.SendAsync(user.Email!, brief.FirstName, subject, RenderHtml(brief, config, studio));
                sent = true;
            }
            catch (Exception ex) { logger.LogWarning(ex, "Nie udało się wysłać porannego briefu do {User}.", user.Id); }
        }

        if (p.PushMorningBrief && (await push.GetSettingsAsync()).IsConfigured && await push.GetSubscriptionCountAsync(user.Id) > 0)
        {
            await push.SendAsync(user.Id, new PushMessageDto
            {
                Title = config?.Name is { Length: > 0 } n ? $"{n}: Twój dzień" : "Twój dzień",
                Body = brief.Intro,
                Url = "/calendar",
                Category = NotificationTypes.PushMorningBrief
            });
            sent = true;
        }
        return sent;
    }

    /// <summary>Szablon e-maila: tabele i style w linii (Gmail, Outlook), obrazek maskotki z Portalu.</summary>
    public static string RenderHtml(Brief b, PlatformAssistantService.Config? config, string? studio)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var name = config?.Name is { Length: > 0 } n ? n : "Hantly";
        var mascot = config?.EmailIcons?.GetValueOrDefault(b.Todo.Count > 0 ? "remind" : "hello");
        var appUrl = AppUrls.PublicBase() ?? "";
        var greeting = b.FirstName.Length > 0 ? $"Dzień dobry, {E(b.FirstName)}!" : "Dzień dobry!";
        var date = b.Day.ToString("dddd, d MMMM", Pl);

        var sessionsHtml = b.Sessions.Count == 0
            ? """<p style="margin:0;color:#6b7280;font-size:14px">Dziś bez treningów w grafiku.</p>"""
            : string.Concat(b.Sessions.Select(s => $"""
                <tr>
                  <td style="padding:10px 0;border-bottom:1px solid #eef0f4;width:62px;vertical-align:top">
                    <span style="display:inline-block;padding:4px 8px;border-radius:8px;background:#f3f0ff;color:#5b21b6;font-weight:700;font-size:13px">{s.Start:HH:mm}</span>
                  </td>
                  <td style="padding:10px 0;border-bottom:1px solid #eef0f4;vertical-align:top">
                    <div style="font-weight:700;color:#111827;font-size:15px">{E(s.Client)}</div>
                    <div style="color:#6b7280;font-size:13px">{E(s.Type)}</div>
                    {(s.Flags.Count == 0 ? "" : "<div style=\"margin-top:5px\">" + string.Concat(s.Flags.Select(f =>
                        $"<span style=\"display:inline-block;margin:0 4px 4px 0;padding:2px 8px;border-radius:999px;background:{(f.Contains("ankiecie") || f.Contains("płatność") ? "#fef3c7;color:#92400e" : "#dcfce7;color:#166534")};font-size:12px;font-weight:600\">{E(f)}</span>")) + "</div>")}
                  </td>
                </tr>
                """));

        string TipRows(IEnumerable<AssistantTipsService.Tip> list, string bullet) => string.Concat(list.Select(t =>
        {
            var text = t.Fallback;
            foreach (var (k, v) in t.Placeholders ?? []) text = text.Replace(k, v);
            var link = t.ActionHref is { } href && appUrl.Length > 0
                ? $" <a href=\"{E(appUrl + href)}\" style=\"color:#6d28d9;font-weight:600;text-decoration:none\">{E(t.ActionLabel)} →</a>"
                : "";
            return $"<tr><td style=\"padding:6px 0;vertical-align:top;width:22px;font-size:15px\">{bullet}</td><td style=\"padding:6px 0;color:#374151;font-size:14px;line-height:1.45\">{E(text)}{link}</td></tr>";
        }));

        var todoHtml = b.Todo.Count == 0 ? "" : $"""
            <tr><td style="padding:22px 28px 0">
              <div style="font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:#92400e;margin-bottom:6px">Do załatwienia</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0">{TipRows(b.Todo, "📌")}</table>
            </td></tr>
            """;
        var praiseHtml = b.Praise.Count == 0 ? "" : $"""
            <tr><td style="padding:22px 28px 0">
              <div style="font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:#166534;margin-bottom:6px">Powody do dumy</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0">{TipRows(b.Praise, "🏆")}</table>
            </td></tr>
            """;
        var button = appUrl.Length == 0 ? "" : $"""
            <tr><td style="padding:26px 28px 6px" align="left">
              <a href="{E(appUrl + "/calendar")}" style="display:inline-block;padding:12px 22px;border-radius:12px;background:#6d28d9;color:#ffffff;font-weight:700;font-size:15px;text-decoration:none">Otwórz grafik</a>
            </td></tr>
            """;
        var settings = appUrl.Length == 0 ? "" : $"""<a href="{E(appUrl + "/Account/Manage/Notifications")}" style="color:#6b7280">zmień w ustawieniach powiadomień</a>""";
        var mascotCell = mascot is null ? "" : $"""<td style="width:88px;vertical-align:bottom;padding-right:14px"><img src="{E(mascot)}" width="80" height="80" alt="{E(name)}" style="display:block;border:0"></td>""";

        return $"""
            <!DOCTYPE html>
            <html lang="pl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Twój dzień</title></head>
            <body style="margin:0;padding:0;background:#f4f2fb;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f2fb;padding:24px 12px">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 6px 24px rgba(76,29,149,.08)">
                    <tr><td style="background:linear-gradient(135deg,#7c3aed,#5b21b6);background-color:#6d28d9;padding:22px 28px">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0"><tr>
                        {mascotCell}
                        <td style="vertical-align:middle">
                          <div style="color:#ddd6fe;font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase">{E(name)} · {E(date)}</div>
                          <div style="color:#ffffff;font-size:22px;font-weight:800;margin-top:4px">{greeting}</div>
                        </td>
                      </tr></table>
                    </td></tr>
                    <tr><td style="padding:22px 28px 0">
                      <div style="background:#f5f3ff;border-radius:14px;padding:14px 16px;color:#1f2937;font-size:15px;line-height:1.5">{E(b.Intro)}</div>
                    </td></tr>
                    <tr><td style="padding:22px 28px 0">
                      <div style="font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:#5b21b6;margin-bottom:4px">Dziś w grafiku</div>
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0">{sessionsHtml}</table>
                    </td></tr>
                    {todoHtml}
                    {praiseHtml}
                    {button}
                    <tr><td style="padding:18px 28px 24px;color:#9ca3af;font-size:12px;line-height:1.5">
                      {E(studio)} · poranny brief od {E(name)}. Nie chcesz go dostawać? {settings}
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;
    }

    /// <summary>Kto dostaje brief: trenerzy (swój dzień) i właściciel studia z zespołem (całe studio); bez dubli po e-mailu.</summary>
    public async Task<List<(ApplicationUser User, bool StudioWide)>> RecipientsAsync()
    {
        var trainers = await userManager.GetUsersInRoleAsync(Roles.Trainer);
        var admins = (await userManager.GetUsersInRoleAsync(Roles.Admin)).ToList();
        var roots = (await userManager.GetUsersInRoleAsync(Roles.Root)).Select(u => u.Id).ToHashSet();
        var result = new List<(ApplicationUser, bool)>();
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trainers)
            if (t.Email is { Length: > 0 } e && emails.Add(e)) result.Add((t, false));
        if (trainers.Count > 1)
            foreach (var a in admins.Where(a => !roots.Contains(a.Id)))
                if (a.Email is { Length: > 0 } e && emails.Add(e)) result.Add((a, true));
        return result;
    }
}

/// <summary>Co 10 minut: kto ma już swoją godzinę briefu, a dziś go nie dostał — dostaje teraz.</summary>
public class MorningBriefBackgroundService(IServiceScopeFactory scopeFactory, ILogger<MorningBriefBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await RunAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Pętla porannych briefów nie powiodła się."); }
            await Task.Delay(TimeSpan.FromMinutes(10), ct);
        }
    }

    private async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IAppClock>();
        var now = clock.LocalNow;
        if (now.Hour < 5 || now.Hour > 12) return;
        var brief = scope.ServiceProvider.GetRequiredService<MorningBriefService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var today = clock.Today;
        foreach (var (user, studioWide) in await brief.RecipientsAsync())
        {
            var p = await db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == user.Id);
            if (p is { MorningBrief: false, PushMorningBrief: false }) continue;
            if (p?.MorningBriefSentOn == today || now.Hour < (p?.MorningBriefHour ?? 7)) continue;
            if (p is null) db.NotificationPreferences.Add(p = new NotificationPreferences { UserId = user.Id });
            p.MorningBriefSentOn = today; // najpierw znacznik — awaria wysyłki nie powoduje lawiny powtórek
            await db.SaveChangesAsync();
            try { await brief.SendAsync(user, studioWide); }
            catch (Exception ex) { logger.LogWarning(ex, "Poranny brief dla {User} nie wyszedł.", user.Id); }
        }
    }
}
