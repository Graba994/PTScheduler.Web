using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>Reguły programu testerów bez bazy — testowane osobno.</summary>
public static class TesterRules
{
    public const int DefaultDays = 90;

    /// <summary>Tydzień programu: 0 w pierwszych 7 dniach, potem 1, 2…</summary>
    public static int Week(DateTime since, DateTime now) => now <= since ? 0 : (int)((now - since).TotalDays / 7);

    // Dzień zapasu — po przedłużeniu o kilka godzin nie przybywa „pustego” tygodnia.
    public static int TotalWeeks(DateTime since, DateTime until) => Math.Max(1, (int)Math.Ceiling(((until - since).TotalDays - 1) / 7));

    public static bool IsActive(Tenant t, DateTime now) => t.TesterSince is not null && t.TesterUntil > now;

    /// <summary>Opinia za bieżący tydzień — od końca pierwszego tygodnia, do końca programu.</summary>
    public static bool FeedbackDue(Tenant t, DateTime now, IEnumerable<int> weeksWithFeedback) =>
        IsActive(t, now) && Week(t.TesterSince!.Value, now) is var w && w >= 1 && !weeksWithFeedback.Contains(w);

    /// <summary>Ile tygodni (od 1 do bieżącego) zostało bez opinii.</summary>
    public static int Missed(Tenant t, DateTime now, IEnumerable<int> weeksWithFeedback)
    {
        if (t.TesterSince is null) return 0;
        var end = t.TesterUntil is { } u && u < now ? u : now;
        var weeks = Week(t.TesterSince.Value, end);
        var given = weeksWithFeedback.ToHashSet();
        return Enumerable.Range(1, Math.Max(0, weeks)).Count(w => !given.Contains(w));
    }

    public static DateTime Later(DateTime? a, DateTime b) => a is { } x && x > b ? x : b;
}

/// <summary>Program testerów: start (kodem z programu albo ręcznie), przedłużenie, koniec i cotygodniowe opinie.</summary>
public class TesterService(IDbContextFactory<PortalDbContext> dbFactory, SiteSettingsService settings, TenantService tenants, ILogger<TesterService> logger)
{
    public async Task StartAsync(int tenantId, int days, string who)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.Include(x => x.Plan).FirstAsync(x => x.Id == tenantId);
        var now = DateTime.UtcNow;
        t.TesterSince ??= now;
        t.TesterUntil = TesterRules.Later(t.TesterUntil, now.AddDays(Math.Clamp(days, 7, 365)));
        await AlignTrialAsync(t);
        db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.PlanChanged, Detail = $"Program testerów do {t.TesterUntil.Value.ToLocalTime():dd.MM.yyyy} ({who})" });
        await db.SaveChangesAsync();
        await PushAsync(t.Id);
    }

    public async Task ExtendAsync(int tenantId, int days, string who)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.Include(x => x.Plan).FirstAsync(x => x.Id == tenantId);
        t.TesterSince ??= DateTime.UtcNow;
        t.TesterUntil = TesterRules.Later(t.TesterUntil, DateTime.UtcNow).AddDays(days);
        await AlignTrialAsync(t);
        db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.PlanChanged, Detail = $"Program testerów przedłużony do {t.TesterUntil.Value.ToLocalTime():dd.MM.yyyy} ({who})" });
        await db.SaveChangesAsync();
        await PushAsync(t.Id);
    }

    /// <summary>Koniec programu od teraz — okres próbny planu zostaje, kończy się sam.</summary>
    public async Task EndAsync(int tenantId, string who)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.FirstAsync(x => x.Id == tenantId);
        t.TesterUntil = DateTime.UtcNow;
        db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.PlanChanged, Detail = $"Koniec programu testerów ({who})" });
        await db.SaveChangesAsync();
        await PushAsync(t.Id);
    }

    /// <summary>
    /// Tester ma plan z okresu próbnego (np. Pro) do końca programu: darmowy plan — okres próbny wyższego planu,
    /// płatny bez karty — okres próbny rozliczeń (bez rachunków i blokady).
    /// </summary>
    private async Task AlignTrialAsync(Tenant t)
    {
        var until = t.TesterUntil!.Value;
        if (t.Plan is { MonthlyPrice: <= 0 })
        {
            var trialId = t.TrialPlanId ?? await settings.GetAsync(SiteSettingsService.Keys.TrialPlanId);
            if (!string.IsNullOrWhiteSpace(trialId) && trialId != t.PlanId)
            {
                t.TrialPlanId = trialId;
                t.TrialPlanEndsAt = TesterRules.Later(t.TrialPlanEndsAt, until);
            }
        }
        else if (t.StripeSubscriptionId is null)
            t.TrialEndsAt = TesterRules.Later(t.TrialEndsAt, until);
    }

    private async Task PushAsync(int tenantId)
    {
        try { await tenants.PushEntitlementsAsync(tenantId); }
        catch (Exception ex) { logger.LogWarning(ex, "Status testera studia {Id} nie wysłany — aplikacja pobierze go sama.", tenantId); }
    }

    public sealed record Submit(int Week, int Rating, string? Annoyed, string? Missing);

    public async Task<(bool Ok, string? Error)> SubmitAsync(int tenantId, Submit body)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tenantId);
        if (t.TesterSince is null) return (false, "Studio nie jest w programie testerów.");
        var current = TesterRules.Week(t.TesterSince.Value, DateTime.UtcNow);
        var week = body.Week is >= 1 && body.Week <= current ? body.Week : Math.Max(1, current);
        if (body.Rating is < 1 or > 5) return (false, "Ocena od 1 do 5.");
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(2000, s.Trim().Length)];
        var existing = await db.TesterFeedback.FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Week == week);
        if (existing is null)
            db.TesterFeedback.Add(new TesterFeedback { TenantId = tenantId, Week = week, Rating = body.Rating, Annoyed = Clean(body.Annoyed), Missing = Clean(body.Missing) });
        else
        {
            existing.Rating = body.Rating;
            existing.Annoyed = Clean(body.Annoyed);
            existing.Missing = Clean(body.Missing);
            existing.CreatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>Do uprawnień studia: status testera dla karty z ankietą w aplikacji.</summary>
    public static async Task<object?> EntitlementAsync(PortalDbContext db, Tenant tenant)
    {
        if (tenant.TesterSince is null || tenant.TesterUntil is null) return null;
        var now = DateTime.UtcNow;
        var weeks = await db.TesterFeedback.AsNoTracking().Where(f => f.TenantId == tenant.Id).Select(f => f.Week).ToListAsync();
        return new
        {
            since = tenant.TesterSince,
            until = tenant.TesterUntil,
            active = TesterRules.IsActive(tenant, now),
            week = TesterRules.Week(tenant.TesterSince.Value, now),
            weeks = TesterRules.TotalWeeks(tenant.TesterSince.Value, tenant.TesterUntil.Value),
            feedbackDue = TesterRules.FeedbackDue(tenant, now, weeks)
        };
    }

    public sealed record Row(Tenant Tenant, List<TesterFeedback> Feedback, int Week, int Weeks, bool Due, int Missed, double? AvgRating);

    public async Task<List<Row>> ListAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var testers = await db.Tenants.AsNoTracking().Where(t => t.TesterSince != null && t.Status != TenantStatus.Destroyed).OrderBy(t => t.TesterUntil).ToListAsync();
        var ids = testers.Select(t => t.Id).ToList();
        var feedback = await db.TesterFeedback.AsNoTracking().Where(f => ids.Contains(f.TenantId)).OrderByDescending(f => f.Week).ToListAsync();
        return testers.Select(t =>
        {
            var fb = feedback.Where(f => f.TenantId == t.Id).ToList();
            var weeks = fb.Select(f => f.Week).ToList();
            return new Row(t, fb, TesterRules.Week(t.TesterSince!.Value, now), TesterRules.TotalWeeks(t.TesterSince.Value, t.TesterUntil ?? now),
                TesterRules.FeedbackDue(t, now, weeks), TesterRules.Missed(t, now, weeks), fb.Count == 0 ? null : Math.Round(fb.Average(f => f.Rating), 1));
        }).ToList();
    }
}

public static class TesterEndpoints
{
    public static void MapTesterFeedback(this WebApplication app) =>
        app.MapPost("/api/internal/tenants/{slug}/tester-feedback", async (string slug, TesterService.Submit body, HttpContext ctx,
            IDbContextFactory<PortalDbContext> dbf, IConfiguration config, TesterService testers) =>
        {
            await using var db = await dbf.CreateDbContextAsync();
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
            if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
            var (ok, error) = await testers.SubmitAsync(tenant.Id, body);
            return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error });
        });
}
