using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public sealed record MailRelayRequest(string To, string? ToName, string Subject, string Html, string? FromName, string? ReplyTo);

/// <summary>
/// Poczta instancji trenerów przez SMTP platformy — Portal wysyła w ich imieniu.
/// Instancje nie dostają hasła do serwera, a każda ma dzienny limit, więc jeden
/// trener (albo przejęta instancja) nie wyczerpie limitów i reputacji serwera
/// pozostałym.
/// </summary>
public class TenantMailRelay(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    EmailService email,
    ILogger<TenantMailRelay> logger)
{
    public const int DefaultDailyLimit = 300;
    private const int MaxHtmlChars = 512 * 1024;

    public async Task<bool> IsEnabledAsync()
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.ShareSmtpWithTenants, SiteSettingsService.Keys.SmtpHost);
        return s.GetValueOrDefault(SiteSettingsService.Keys.ShareSmtpWithTenants) != "false"
            && !string.IsNullOrWhiteSpace(s.GetValueOrDefault(SiteSettingsService.Keys.SmtpHost));
    }

    public async Task<int> GetDailyLimitAsync() =>
        int.TryParse(await settings.GetAsync(SiteSettingsService.Keys.TenantMailDailyLimit), out var l) && l > 0 ? l : DefaultDailyLimit;

    public async Task<int> SentTodayAsync(int tenantId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = dbFactory.CreateDbContext();
        return await db.TenantMailCounters.Where(c => c.TenantId == tenantId && c.Day == today).Select(c => c.Count).FirstOrDefaultAsync();
    }

    public async Task<Dictionary<int, int>> SentTodayByTenantAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = dbFactory.CreateDbContext();
        return await db.TenantMailCounters.Where(c => c.Day == today).ToDictionaryAsync(c => c.TenantId, c => c.Count);
    }

    /// <returns>Kod HTTP do zwrócenia instancji: 200, 400 (zła wiadomość), 429 (limit), 503 (brak SMTP), 502 (błąd serwera).</returns>
    public async Task<(int Status, string? Error)> SendAsync(Tenant tenant, MailRelayRequest r)
    {
        if (!await IsEnabledAsync()) return (503, "Poczta platformy jest wyłączona.");
        if (!IsSingleAddress(r.To)) return (400, "Nieprawidłowy adres odbiorcy.");
        if (!string.IsNullOrWhiteSpace(r.ReplyTo) && !IsSingleAddress(r.ReplyTo)) return (400, "Nieprawidłowy adres odpowiedzi.");
        if (string.IsNullOrWhiteSpace(r.Subject) || string.IsNullOrEmpty(r.Html)) return (400, "Brak tematu albo treści.");
        if (r.Html.Length > MaxHtmlChars) return (400, "Wiadomość jest za duża.");

        var limit = await GetDailyLimitAsync();
        var count = await ReserveAsync(tenant.Id);
        if (count > limit)
        {
            if (count == limit + 1)
                logger.LogWarning("Poczta: instancja {Slug} osiągnęła dzienny limit {Limit} e-maili.", tenant.Slug, limit);
            return (429, $"Dzienny limit e-maili ({limit}) został wyczerpany — wysyłka wróci jutro.");
        }

        var (ok, error) = await email.SendAsync(r.To.Trim(), Clean(r.Subject, 300), r.Html,
            fromNameOverride: Clean(r.FromName, 80) is { Length: > 0 } name ? name : tenant.CompanyName,
            replyTo: string.IsNullOrWhiteSpace(r.ReplyTo) ? null : r.ReplyTo.Trim(),
            toName: Clean(r.ToName, 120));
        return ok ? (200, null) : (502, error ?? "Serwer poczty odrzucił wiadomość.");
    }

    /// <summary>Atomowy licznik dnia (INSERT … ON CONFLICT) — równoległe wysyłki liczą się poprawnie.</summary>
    private async Task<int> ReserveAsync(int tenantId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.Database.SqlQuery<int>($"""
            INSERT INTO "TenantMailCounters" ("TenantId", "Day", "Count") VALUES ({tenantId}, {today}, 1)
            ON CONFLICT ("TenantId", "Day") DO UPDATE SET "Count" = "TenantMailCounters"."Count" + 1
            RETURNING "Count" AS "Value"
            """).ToListAsync();
        return rows.FirstOrDefault();
    }

    private static bool IsSingleAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 || value.Contains(',') || value.Contains(';')) return false;
        try { return new MailAddress(value.Trim()).Address == value.Trim(); }
        catch { return false; }
    }

    /// <summary>Bez znaków nowej linii (wstrzykiwanie nagłówków) i z limitem długości.</summary>
    private static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var v = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return v.Length > max ? v[..max] : v;
    }
}
