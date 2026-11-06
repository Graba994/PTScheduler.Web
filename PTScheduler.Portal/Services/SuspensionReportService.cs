using System.Net;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>Powody zawieszenia zapisywane w historii studia (TenantEvent.Detail) — z nich raport zawieszeń.</summary>
public static class SuspensionReasons
{
    public const string TrialExpired = "Koniec okresu próbnego bez płatności";
    public const string Cancelled = "Rezygnacja trenera — koniec opłaconego okresu";
    public const string StripeCancelled = "Subskrypcja Stripe anulowana (karta albo rezygnacja)";
    public const string Other = "Bez podanego powodu";

    public static string Inactive(int days) => $"Nieużywane od {days} dni";
    public static string Manual(string? who) => $"Ręcznie w Portalu{(string.IsNullOrWhiteSpace(who) ? "" : $" ({who})")}";

    /// <summary>Rodzaj do grupowania w raporcie.</summary>
    public static string Kind(string? detail) => detail switch
    {
        null or "" or Other => "Inne",
        TrialExpired => "Okres próbny",
        Cancelled or StripeCancelled => "Rezygnacja / płatność",
        _ when detail.StartsWith("Nieużywane", StringComparison.Ordinal) => "Brak aktywności",
        _ when detail.StartsWith("Ręcznie", StringComparison.Ordinal) => "Ręcznie",
        _ => "Inne"
    };
}

/// <summary>
/// Raport zawieszeń dla administratora: co zostało zawieszone od ostatniego raportu (z powodem: brak aktywności,
/// koniec okresu próbnego, rezygnacja, ręcznie…) i co zostanie zawieszone w najbliższych dniach. Raz dziennie rano,
/// tylko gdy jest o czym pisać. Zawieszone studia nie wywołują już alarmów „nie odpowiada”.
/// </summary>
public class SuspensionReportService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    EmailService email,
    IConfiguration config)
{
    public const int SoonDays = 3;
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public sealed record Item(int TenantId, string Name, string Slug, string Reason, DateTime At);
    public sealed record Soon(int TenantId, string Name, string Slug, string Reason, DateTime When);
    public sealed record Report(List<Item> Suspended, List<Soon> Upcoming, int TotalSuspended, DateTime Since);

    public async Task<Report> BuildAsync(DateTime since, DateTime now)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var events = await db.TenantEvents.AsNoTracking()
            .Where(e => e.EventType == TenantEventTypes.Suspended && e.OccurredAt >= since)
            .OrderBy(e => e.OccurredAt).ToListAsync();
        var ids = events.Select(e => e.TenantId).Distinct().ToList();
        var names = await db.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => (t.CompanyName, t.Slug));
        var suspended = events.Select(e => new Item(e.TenantId, names.GetValueOrDefault(e.TenantId).CompanyName ?? $"#{e.TenantId}",
            names.GetValueOrDefault(e.TenantId).Slug ?? "", e.Detail ?? SuspensionReasons.Other, e.OccurredAt)).ToList();

        var upcoming = new List<Soon>();
        var active = await db.Tenants.AsNoTracking().Include(t => t.Plan).Where(t => t.Status == TenantStatus.Active).ToListAsync();
        foreach (var t in active)
        {
            if (t.GraceUntil > now || t.TesterUntil > now) continue;
            // Brak aktywności — te same progi co TenantCleanupService.
            if (t.LastActivityAt is { } last)
            {
                var free = t.Plan is { MonthlyPrice: <= 0 } && !t.IsTestAccount;
                var suspendDays = free ? config.GetValue("TenantLifecycle:FreeInactivitySuspendDays", 14) : config.GetValue("TenantLifecycle:InactivitySuspendDays", 21);
                var at = last.AddDays(suspendDays);
                if (at <= now.AddDays(SoonDays))
                    upcoming.Add(new Soon(t.Id, t.CompanyName, t.Slug, $"Brak aktywności od {(int)(now - last).TotalDays} dni", at < now ? now : at));
            }
            // Koniec okresu próbnego bez karty i bez rozliczenia rachunkami.
            if (t.Plan is { MonthlyPrice: > 0 } && t.StripeSubscriptionId == null && t.BillingStatus is not ("verified" or "active" or "past_due")
                && t.TrialEndsAt is { } trial && trial > now && trial <= now.AddDays(SoonDays))
                upcoming.Add(new Soon(t.Id, t.CompanyName, t.Slug, "Koniec okresu próbnego bez płatności", trial));
        }

        var total = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Suspended);
        return new Report(suspended, upcoming.OrderBy(u => u.When).ToList(), total, since);
    }

    public string Html(Report r)
    {
        var portal = PortalUrls.Public(config);
        // Serwer działa w UTC — godziny w e-mailu po polsku.
        DateTime Pl(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Warsaw);
        string Link(int id, string name) => $"<a href=\"{WebUtility.HtmlEncode($"{portal}/panel/tenants/{id}")}\">{WebUtility.HtmlEncode(name)}</a>";
        var sb = new System.Text.StringBuilder();
        sb.Append("<div style=\"font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:620px;margin:0 auto;color:#0f172a\">");
        sb.Append("<h2 style=\"margin:0 0 4px\">Raport zawieszeń</h2>");
        sb.Append($"<p style=\"color:#64748b;margin:0 0 16px\">Od {Pl(r.Since):dd.MM HH:mm} · zawieszonych studiów łącznie: {r.TotalSuspended}</p>");
        if (r.Suspended.Count > 0)
        {
            sb.Append($"<h3 style=\"margin:16px 0 6px\">Zawieszone ({r.Suspended.Count})</h3>");
            foreach (var g in r.Suspended.GroupBy(i => SuspensionReasons.Kind(i.Reason)))
            {
                sb.Append($"<p style=\"margin:10px 0 4px;font-weight:700\">{WebUtility.HtmlEncode(g.Key)}</p><ul style=\"margin:0;padding-left:18px\">");
                foreach (var i in g)
                    sb.Append($"<li style=\"margin-bottom:4px\">{Link(i.TenantId, i.Name)} <span style=\"color:#64748b\">({WebUtility.HtmlEncode(i.Slug)}) — {WebUtility.HtmlEncode(i.Reason)}, {Pl(i.At):dd.MM HH:mm}</span></li>");
                sb.Append("</ul>");
            }
        }
        if (r.Upcoming.Count > 0)
        {
            sb.Append($"<h3 style=\"margin:18px 0 6px\">Zawieszenie w ciągu {SoonDays} dni ({r.Upcoming.Count})</h3><ul style=\"margin:0;padding-left:18px\">");
            foreach (var u in r.Upcoming)
                sb.Append($"<li style=\"margin-bottom:4px\">{Link(u.TenantId, u.Name)} <span style=\"color:#64748b\">— {WebUtility.HtmlEncode(u.Reason)}, około {Pl(u.When):dd.MM}</span></li>");
            sb.Append("</ul><p style=\"color:#64748b;font-size:13px\">Okres ochronny w szczegółach studia wstrzymuje automatyczne zawieszenie.</p>");
        }
        sb.Append("<p style=\"color:#94a3b8;font-size:12px;margin-top:20px\">Zawieszone studia nie wywołują alarmów dostępności. Raport przychodzi rano, tylko gdy coś się wydarzyło albo zbliża.</p></div>");
        return sb.ToString();
    }

    /// <summary>Wysyła raport, jeśli dziś jeszcze nie poszedł i jest o czym pisać. Zwraca, czy wysłał.</summary>
    public async Task<bool> SendDailyAsync(DateTime now, bool force = false)
    {
        var lastRaw = await settings.GetAsync(SiteSettingsService.Keys.SuspensionReportSentAt);
        DateTime? last = DateTime.TryParse(lastRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var l) ? l.ToUniversalTime() : null;
        if (!force && last?.Date == now.Date) return false;
        var report = await BuildAsync(last ?? now.AddDays(-1), now);
        await settings.SetAsync(SiteSettingsService.Keys.SuspensionReportSentAt, now.ToString("O"));
        if (!force && report.Suspended.Count == 0 && report.Upcoming.Count == 0) return false;
        var to = await settings.GetAsync(SiteSettingsService.Keys.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return false;
        var subject = report.Suspended.Count > 0
            ? $"Zawieszone: {report.Suspended.Count}{(report.Upcoming.Count > 0 ? $", wkrótce: {report.Upcoming.Count}" : "")} — raport Hantly"
            : $"Wkrótce zawieszenie: {report.Upcoming.Count} — raport Hantly";
        var (ok, _) = await email.SendAsync(to, subject, Html(report));
        return ok;
    }
}

/// <summary>Raport zawieszeń codziennie około 8:00 (czas polski).</summary>
public class SuspensionReportBackgroundService(IServiceScopeFactory scopes, ILogger<SuspensionReportBackgroundService> logger) : BackgroundService
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(5), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Warsaw);
                if (local.Hour >= 8)
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<SuspensionReportService>().SendDailyAsync(DateTime.UtcNow);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Raport zawieszeń nie powiódł się."); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
