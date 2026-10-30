using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Wywołania modeli AI przez OpenRouter (jeden klucz, wiele modeli — model to jedno ustawienie).
/// Pilnuje włącznika i miesięcznego budżetu, a każde wywołanie zapisuje w AiUsageLogs (tokeny, koszt, błąd).
/// </summary>
public class AiService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<AiService> logger)
{
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
    private IDataProtector Secrets => dataProtection.CreateProtector("ai-api-key");

    public sealed record Result(string? Text, string? Error, decimal CostUsd, string Model);

    public async Task<string?> GetApiKeyAsync()
    {
        var raw = await settings.GetAsync(K.AiApiKey);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); }
        catch (CryptographicException)
        {
            logger.LogWarning("Nie da się odszyfrować klucza AI — klucze Data Protection się zmieniły. Wpisz klucz ponownie w Panelu.");
            return null;
        }
    }

    public Task SetApiKeyAsync(string? key) =>
        settings.SetAsync(K.AiApiKey, string.IsNullOrWhiteSpace(key) ? "" : Secrets.Protect(key.Trim()));

    public async Task<bool> HasApiKeyAsync() => !string.IsNullOrEmpty(await settings.GetAsync(K.AiApiKey));

    public async Task<decimal> SpentThisMonthUsdAsync()
    {
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.AiUsageLogs.Where(l => l.CreatedAt >= start).SumAsync(l => (decimal?)l.CostUsd) ?? 0m;
    }

    /// <summary>Czy AI może teraz odpowiadać: włącznik, klucz, model i budżet. Null = tak, inaczej powód.</summary>
    public async Task<string?> UnavailableReasonAsync(bool ignoreSwitch = false)
    {
        var s = await settings.GetAllAsync(K.AiEnabled, K.AiModel, K.AiMonthlyBudgetUsd);
        if (!ignoreSwitch && s[K.AiEnabled] != "true") return "AI jest wyłączone.";
        if (!await HasApiKeyAsync()) return "Brak klucza OpenRouter.";
        if (string.IsNullOrWhiteSpace(s[K.AiModel])) return "Nie wybrano modelu.";
        if (decimal.TryParse(s[K.AiMonthlyBudgetUsd], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var budget)
            && budget > 0 && await SpentThisMonthUsdAsync() >= budget)
            return $"Wyczerpany budżet miesięczny ({budget:0.##} $).";
        return null;
    }

    /// <summary>Plan studia ma asystenta AI i nie wyczerpał limitu akcji w tym miesiącu. Null = może, inaczej powód.</summary>
    public async Task<string?> TenantUnavailableReasonAsync(int tenantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null || await PlanFeatureCatalog.EffectiveAsync(db, tenant) is not { } plan) return null;
        if (!plan.AiAssistant || plan.MaxAiActionsPerMonth <= 0) return "Asystent AI nie jest dostępny w planie studia.";
        if (plan.MaxAiActionsPerMonth >= PlanFeatureCatalog.Unlimited) return null;
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var used = await db.AiUsageLogs.CountAsync(l => l.TenantId == tenantId && l.Ok && l.CreatedAt >= start);
        return used >= plan.MaxAiActionsPerMonth ? $"Wykorzystano limit akcji AI w tym miesiącu ({plan.MaxAiActionsPerMonth})." : null;
    }

    /// <summary>
    /// Jedno krótkie zapytanie do modelu. Zwraca tekst albo błąd (nigdy wyjątek) — wywołujący
    /// ma zawsze gotowy tekst zapasowy. <paramref name="ignoreSwitch"/> = test z Panelu przy wyłączonym AI.
    /// </summary>
    public async Task<Result> CompleteAsync(string feature, int? tenantId, string system, string user,
        int maxTokens = 220, bool ignoreSwitch = false, CancellationToken ct = default)
    {
        var model = (await settings.GetAsync(K.AiModel)).Trim();
        if (await UnavailableReasonAsync(ignoreSwitch) is { } reason) return new(null, reason, 0, model);
        // Limit planu studia — wywołujący ma tekst zapasowy, więc bez AI wszystko dalej działa.
        if (!ignoreSwitch && tenantId is int tid && await TenantUnavailableReasonAsync(tid) is { } planReason) return new(null, planReason, 0, model);
        var key = await GetApiKeyAsync();
        if (string.IsNullOrEmpty(key)) return new(null, "Brak klucza OpenRouter.", 0, model);

        var log = new AiUsageLog { TenantId = tenantId, Feature = feature, Model = model };
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(new
                {
                    model,
                    messages = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
                    max_tokens = maxTokens,
                    temperature = 0.7,
                    usage = new { include = true }
                })
            };
            req.Headers.Authorization = new("Bearer", key);
            if (config.GetValue<string>("Portal:PublicUrl") is { Length: > 0 } site) req.Headers.TryAddWithoutValidation("HTTP-Referer", site);
            req.Headers.TryAddWithoutValidation("X-Title", await settings.GetAsync(K.BrandName) is { Length: > 0 } brand ? brand : ReferralService.PlatformName);

            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(25);
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.Error = $"HTTP {(int)resp.StatusCode}: {Trim(ErrorMessage(body), 400)}";
                return new(null, log.Error, 0, model);
            }

            var parsed = JsonSerializer.Deserialize<ChatResponse>(body);
            log.PromptTokens = parsed?.Usage?.PromptTokens ?? 0;
            log.CompletionTokens = parsed?.Usage?.CompletionTokens ?? 0;
            log.CostUsd = parsed?.Usage?.Cost ?? await EstimateAsync(log.PromptTokens, log.CompletionTokens);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                log.Error = "Pusta odpowiedź modelu.";
                return new(null, log.Error, log.CostUsd, model);
            }
            log.Ok = true;
            return new(text, null, log.CostUsd, model);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.Error = ex is TaskCanceledException ? "Przekroczony czas odpowiedzi." : Trim(ex.Message, 400);
            return new(null, log.Error, 0, model);
        }
        finally
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
                db.AiUsageLogs.Add(log);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Nie udało się zapisać wywołania AI w dzienniku."); }
        }
    }

    private async Task<decimal> EstimateAsync(int prompt, int completion)
    {
        var s = await settings.GetAllAsync(K.AiPriceInputPerM, K.AiPriceOutputPerM);
        static decimal P(string v) => decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
        return Math.Round((prompt * P(s[K.AiPriceInputPerM]) + completion * P(s[K.AiPriceOutputPerM])) / 1_000_000m, 6);
    }

    private static string ErrorMessage(string body)
    {
        try { return JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("message").GetString() ?? body; }
        catch { return body; }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
        [JsonPropertyName("usage")] public UsageInfo? Usage { get; set; }
    }
    private sealed class Choice { [JsonPropertyName("message")] public Msg? Message { get; set; } }
    private sealed class Msg { [JsonPropertyName("content")] public string? Content { get; set; } }
    private sealed class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("cost")] public decimal? Cost { get; set; }
    }
}
