using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Zgłoszenia błędów z aplikacji trenera (przycisk „Zgłoś błąd”). Instancja wysyła je własnym sekretem
/// (POST /api/internal/tenants/{slug}/bug-reports), administrator dostaje e-mail i przegląda je w Panelu
/// (/panel/bledy). Treść nie trafia do AI — zrzut i komentarz mogą zawierać dane klientów trenera.
/// </summary>
public class BugReportService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    EmailService email,
    IConfiguration config,
    ILogger<BugReportService> logger)
{
    public const int MaxComment = 2000;
    public const int MaxScreenshotBytes = 3 * 1024 * 1024;
    public const int MaxPerTenantPerDay = 30;
    /// <summary>Po tylu dniach od zamknięcia zgłoszenia zrzut ekranu jest usuwany (komentarz zostaje).</summary>
    public const int KeepScreenshotDays = 90;

    public sealed record Incoming(string? Comment, string? PageUrl, string? PageTitle, string? AuthorEmail, string? AuthorName,
        string? AuthorRole, string? UserAgent, string? Viewport, string? AppVersion, string? ClientErrors,
        string? Screenshot, string? ScreenshotType);

    /// <summary>Wiersz listy bez zrzutu (sam zrzut pobiera osobny adres).</summary>
    public sealed record Row(int Id, int TenantId, string Tenant, string Comment, string? PageUrl, string? PageTitle,
        string? AuthorEmail, string? AuthorName, string? AuthorRole, string? UserAgent, string? Viewport, string? AppVersion,
        string? ClientErrors, bool HasScreenshot, BugReportStatus Status, string? AdminNote, DateTime CreatedAt, DateTime? ClosedAt);

    public async Task<(bool Ok, string? Error, int Id)> SaveAsync(Tenant tenant, Incoming body)
    {
        var comment = Clip(body.Comment, MaxComment);
        if (comment is null) return (false, "Opisz, co się stało.", 0);

        byte[]? shot = null;
        var type = body.ScreenshotType is "image/jpeg" or "image/png" or "image/webp" ? body.ScreenshotType : "image/jpeg";
        if (!string.IsNullOrWhiteSpace(body.Screenshot))
        {
            var b64 = body.Screenshot;
            var comma = b64.IndexOf(',');
            if (b64.StartsWith("data:", StringComparison.Ordinal) && comma > 0) b64 = b64[(comma + 1)..];
            try { shot = Convert.FromBase64String(b64); }
            catch (FormatException) { return (false, "Zrzut ekranu jest uszkodzony.", 0); }
            if (shot.Length > MaxScreenshotBytes) return (false, "Zrzut ekranu jest za duży.", 0);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var since = DateTime.UtcNow.AddDays(-1);
        if (await db.BugReports.CountAsync(r => r.TenantId == tenant.Id && r.CreatedAt >= since) >= MaxPerTenantPerDay)
            return (false, "Dziś wysłano już dużo zgłoszeń — spróbuj jutro albo napisz do nas e-mailem.", 0);

        var report = new BugReport
        {
            TenantId = tenant.Id,
            Comment = comment,
            PageUrl = Clip(body.PageUrl, 500),
            PageTitle = Clip(body.PageTitle, 200),
            AuthorEmail = Clip(body.AuthorEmail, 256),
            AuthorName = Clip(body.AuthorName, 120),
            AuthorRole = Clip(body.AuthorRole, 40),
            UserAgent = Clip(body.UserAgent, 400),
            Viewport = Clip(body.Viewport, 40),
            AppVersion = Clip(body.AppVersion, 80),
            ClientErrors = Clip(body.ClientErrors, 8000),
            Screenshot = shot,
            ScreenshotType = shot is null ? null : type,
            CreatedAt = DateTime.UtcNow
        };
        db.BugReports.Add(report);
        await db.SaveChangesAsync();

        // Sprzątanie przy okazji: zrzuty starych, zamkniętych zgłoszeń nie są już potrzebne.
        var old = DateTime.UtcNow.AddDays(-KeepScreenshotDays);
        var stale = await db.BugReports.Where(r => r.ClosedAt != null && r.ClosedAt < old && r.Screenshot != null).Take(50).ToListAsync();
        if (stale.Count > 0)
        {
            foreach (var r in stale) { r.Screenshot = null; r.ScreenshotType = null; }
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Zgłoszenie błędu #{Id} od {Slug} ({Page}).", report.Id, tenant.Slug, report.PageUrl);
        return (true, null, report.Id);
    }

    /// <summary>E-mail do administratora (adres z Konfiguracji → powiadomienia; można wyłączyć na stronie zgłoszeń).</summary>
    public async Task NotifyAsync(int id)
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.AdminNotificationEmail, SiteSettingsService.Keys.NotifyBugsEmail);
        var to = s[SiteSettingsService.Keys.AdminNotificationEmail];
        if (string.IsNullOrWhiteSpace(to) || s[SiteSettingsService.Keys.NotifyBugsEmail] == "false") return;
        var r = await GetAsync(id);
        if (r is null) return;
        try
        {
            var link = $"{PortalUrls.Public(config)}/panel/bledy?id={r.Id}";
            await email.SendAsync(to, $"Zgłoszenie błędu #{r.Id} — {r.Tenant}",
                email.AdminBugReportEmailBody(r.Id, r.Tenant, r.AuthorName ?? r.AuthorEmail ?? "—", r.PageUrl, r.Comment, r.HasScreenshot, link));
        }
        catch (Exception ex) { logger.LogError(ex, "E-mail o zgłoszeniu błędu #{Id} nie wyszedł.", id); }
    }

    public async Task<List<Row>> ListAsync(BugReportStatus? status, int take = 200)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.BugReports.AsNoTracking().AsQueryable();
        if (status is { } st) q = q.Where(r => r.Status == st);
        return await Project(q.OrderByDescending(r => r.CreatedAt).Take(take)).ToListAsync();
    }

    public async Task<Row?> GetAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Project(db.BugReports.AsNoTracking().Where(r => r.Id == id)).FirstOrDefaultAsync();
    }

    public async Task<Dictionary<BugReportStatus, int>> CountsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.BugReports.AsNoTracking().GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        return rows.ToDictionary(x => x.Key, x => x.Count);
    }

    public async Task<(byte[] Data, string Type)?> ScreenshotAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var r = await db.BugReports.AsNoTracking().Where(x => x.Id == id && x.Screenshot != null)
            .Select(x => new { x.Screenshot, x.ScreenshotType }).FirstOrDefaultAsync();
        return r?.Screenshot is null ? null : (r.Screenshot, r.ScreenshotType ?? "image/jpeg");
    }

    /// <summary>Zmiana stanu; przy „Naprawione” opcjonalnie e-mail do osoby, która zgłosiła błąd.</summary>
    public async Task<(bool Ok, string Message)> SetStatusAsync(int id, BugReportStatus status, string? note, bool tellAuthor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var r = await db.BugReports.Include(x => x.Tenant).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return (false, "Nie znaleziono zgłoszenia.");
        r.Status = status;
        r.AdminNote = Clip(note, 2000);
        r.ClosedAt = status is BugReportStatus.Fixed or BugReportStatus.Closed ? r.ClosedAt ?? DateTime.UtcNow : null;
        await db.SaveChangesAsync();

        if (status != BugReportStatus.Fixed || !tellAuthor || string.IsNullOrWhiteSpace(r.AuthorEmail))
            return (true, "Zapisano.");
        try
        {
            await email.SendAsync(r.AuthorEmail, "Naprawiliśmy zgłoszony błąd",
                email.BugFixedEmailBody(r.AuthorName, r.Comment, r.AdminNote));
            return (true, $"Zapisano i wysłano e-mail do {r.AuthorEmail}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "E-mail o naprawie błędu #{Id} nie wyszedł.", id);
            return (true, "Zapisano, ale e-mail do zgłaszającego nie wyszedł.");
        }
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.BugReports.FindAsync(id) is not { } r) return;
        db.BugReports.Remove(r);
        await db.SaveChangesAsync();
    }

    private static IQueryable<Row> Project(IQueryable<BugReport> q) => q.Select(r => new Row(
        r.Id, r.TenantId, r.Tenant!.CompanyName ?? r.Tenant.Slug, r.Comment, r.PageUrl, r.PageTitle,
        r.AuthorEmail, r.AuthorName, r.AuthorRole, r.UserAgent, r.Viewport, r.AppVersion,
        r.ClientErrors, r.Screenshot != null, r.Status, r.AdminNote, r.CreatedAt, r.ClosedAt));

    private static string? Clip(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        return v.Length > max ? v[..max] : v;
    }
}
