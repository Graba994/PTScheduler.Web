using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// SMS reminders via SMSAPI.pl (https://www.smsapi.pl). This is a bring-your-own-account
/// integration — the trainer pays SMSAPI.pl directly for message costs; the platform only
/// enforces a monthly send quota tied to the subscription plan.
/// </summary>
public class SmsApiService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<SmsApiService> logger) : ISmsService
{
    private const string ApiUrl = "https://api.smsapi.pl/sms.do";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<bool> IsEnabledAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();
        return s is { IsEnabled: true } && !string.IsNullOrWhiteSpace(s.ApiToken);
    }

    public async Task<(bool Success, string? Error)> TestAsync(string phone)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();
        if (s is null || string.IsNullOrWhiteSpace(s.ApiToken))
            return (false, "Brak skonfigurowanego tokenu API SMSAPI.pl.");

        return await SendRawAsync(s.ApiToken, s.SenderName, phone,
            "Test połączenia SMS — PTScheduler. Konfiguracja działa poprawnie.");
    }

    public async Task<SmsResult> SendReminderAsync(string phone, string message, int maxPerMonth)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();
        if (s is null || !s.IsEnabled || string.IsNullOrWhiteSpace(s.ApiToken))
            return new SmsResult(false, false, "SMS wyłączone lub brak konfiguracji.");

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

    public async Task<(int Sent, int Max)> GetQuotaStatusAsync(int maxPerMonth)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();
        if (s is null) return (0, maxPerMonth);
        RollQuotaIfNeeded(s);
        await db.SaveChangesAsync();
        return (s.QuotaSentCount, maxPerMonth);
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

    // Accepts "123456789", "+48123456789", "48 123 456 789", etc. Assumes PL (+48)
    // when only 9 digits are given, which covers the overwhelming majority of clients.
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
