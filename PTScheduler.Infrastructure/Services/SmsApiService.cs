using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SmsApiService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<SmsApiService> logger) : ISmsService
{
    private const string ApiUrl = "https://api.smsapi.pl/sms.do";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private string? PortalUrl => Environment.GetEnvironmentVariable("PORTAL_URL");
    private string? TenantSlug => Environment.GetEnvironmentVariable("TENANT_SLUG");
    private string? InternalSecret => Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");

    public async Task<bool> IsEnabledAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();
        if (s is { IsEnabled: true } && !string.IsNullOrWhiteSpace(s.ApiToken))
            return true;

        return await IsPlatformSmsEnabledAsync();
    }

    public async Task<(bool Success, string? Error)> TestAsync(string phone)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();

        if (s is not null && !string.IsNullOrWhiteSpace(s.ApiToken))
        {
            return await SendRawAsync(s.ApiToken, s.SenderName, phone,
                "Test połączenia SMS — PTScheduler. Konfiguracja działa poprawnie.");
        }

        return await TestViaPlatformAsync(phone);
    }

    public async Task<SmsResult> SendReminderAsync(string phone, string message, int maxPerMonth)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();

        if (s is not null && s.IsEnabled && !string.IsNullOrWhiteSpace(s.ApiToken))
        {
            RollQuotaIfNeeded(s);

            if (maxPerMonth != int.MaxValue && s.QuotaSentCount >= maxPerMonth)
                return new SmsResult(false, true, "Przekroczono miesięczny limit SMS dla tego planu.");

            var (ok, error) = await SendRawAsync(s.ApiToken, s.SenderName, phone, message);
            if (ok)
            {
                s.QuotaSentCount++;
                await db.SaveChangesAsync();
            }
            return new SmsResult(ok, false, error);
        }

        return await SendViaPlatformAsync(phone, message);
    }

    public async Task<(int Sent, int Max)> GetQuotaStatusAsync(int maxPerMonth)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();

        if (s is not null && !string.IsNullOrWhiteSpace(s.ApiToken))
        {
            RollQuotaIfNeeded(s);
            await db.SaveChangesAsync();
            return (s.QuotaSentCount, maxPerMonth);
        }

        var credits = await GetPlatformSmsCreditsAsync();
        if (credits >= 0)
            return (0, (int)credits);

        if (s is null) return (0, maxPerMonth);
        RollQuotaIfNeeded(s);
        await db.SaveChangesAsync();
        return (s.QuotaSentCount, maxPerMonth);
    }

    public async Task<CentralizedSmsStatus?> GetCentralizedStatusAsync()
    {
        if (string.IsNullOrEmpty(PortalUrl) || string.IsNullOrEmpty(TenantSlug))
            return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{PortalUrl.TrimEnd('/')}/api/credits/{TenantSlug}");
            if (!string.IsNullOrEmpty(InternalSecret))
                req.Headers.TryAddWithoutValidation("X-Internal-Secret", InternalSecret);

            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            return new CentralizedSmsStatus(
                PlatformSmsEnabled: root.GetProperty("platformSmsEnabled").GetBoolean(),
                SmsCredits: root.GetProperty("sms").GetDecimal());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch centralized SMS status from portal");
            return null;
        }
    }

    private async Task<bool> IsPlatformSmsEnabledAsync()
    {
        var status = await GetCentralizedStatusAsync();
        return status is { PlatformSmsEnabled: true, SmsCredits: > 0 };
    }

    private async Task<decimal> GetPlatformSmsCreditsAsync()
    {
        var status = await GetCentralizedStatusAsync();
        if (status is { PlatformSmsEnabled: true })
            return status.SmsCredits;
        return -1;
    }

    private async Task<SmsResult> SendViaPlatformAsync(string phone, string message)
    {
        if (string.IsNullOrEmpty(PortalUrl) || string.IsNullOrEmpty(TenantSlug))
            return new SmsResult(false, false, "SMS wyłączone lub brak konfiguracji.");

        try
        {
            var payload = JsonSerializer.Serialize(new { phone, message });
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{PortalUrl.TrimEnd('/')}/api/credits/{TenantSlug}/sms/send");
            if (!string.IsNullOrEmpty(InternalSecret))
                req.Headers.TryAddWithoutValidation("X-Internal-Secret", InternalSecret);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var success = root.GetProperty("success").GetBoolean();
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;

            if (!success && error?.Contains("kredyt", StringComparison.OrdinalIgnoreCase) == true)
                return new SmsResult(false, true, error);

            return new SmsResult(success, false, error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Platform SMS send via portal failed");
            return new SmsResult(false, false, "Błąd połączenia z portalem SMS.");
        }
    }

    private async Task<(bool Success, string? Error)> TestViaPlatformAsync(string phone)
    {
        if (string.IsNullOrEmpty(PortalUrl) || string.IsNullOrEmpty(TenantSlug))
            return (false, "Brak konfiguracji platformy SMS.");

        try
        {
            var payload = JsonSerializer.Serialize(new { phone });
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{PortalUrl.TrimEnd('/')}/api/credits/{TenantSlug}/sms/test");
            if (!string.IsNullOrEmpty(InternalSecret))
                req.Headers.TryAddWithoutValidation("X-Internal-Secret", InternalSecret);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var success = root.GetProperty("success").GetBoolean();
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (success, error);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Platform SMS test via portal failed");
            return (false, "Błąd połączenia z portalem SMS.");
        }
    }

    private static void RollQuotaIfNeeded(Domain.Entities.SmsSettings s)
    {
        var monthKey = DateTime.UtcNow.Year * 100 + DateTime.UtcNow.Month;
        if (s.QuotaMonthKey != monthKey)
        {
            s.QuotaMonthKey = monthKey;
            s.QuotaSentCount = 0;
        }
    }

    private async Task<(bool Success, string? Error)> SendRawAsync(string apiToken, string senderName, string phone, string message)
    {
        var normalized = NormalizePhone(phone);
        if (normalized is null) return (false, "Nieprawidłowy numer telefonu.");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            var fields = new Dictionary<string, string>
            {
                ["to"] = normalized,
                ["message"] = message,
                ["format"] = "json"
            };
            if (!string.IsNullOrWhiteSpace(senderName))
                fields["from"] = senderName;
            req.Content = new FormUrlEncodedContent(fields);

            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errProp))
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : $"Błąd SMSAPI ({errProp})";
                return (false, msg);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMSAPI.pl send failed");
            return (false, "Błąd połączenia z SMSAPI.pl.");
        }
    }

    private static string? NormalizePhone(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            9 => "48" + digits,
            11 when digits.StartsWith("48") => digits,
            _ when digits.Length > 9 => digits,
            _ => null
        };
    }
}

