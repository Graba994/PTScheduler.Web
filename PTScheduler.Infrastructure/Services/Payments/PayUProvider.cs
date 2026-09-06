using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PTScheduler.Domain.Constants;

namespace PTScheduler.Infrastructure.Services.Payments;

/// <summary>PayU REST integration (OAuth client_credentials + create order + MD5 notify).</summary>
public sealed class PayUProvider(ILogger<PayUProvider> logger) : IPaymentProvider
{
    private const string SandboxBase = "https://secure.snd.payu.com";
    private const string ProdBase = "https://secure.payu.com";

    // AllowAutoRedirect must be off so we can read PayU's 302 body (create-order
    // returns 302 with redirectUri/orderId in the JSON payload).
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string Key => PaymentProviders.PayU;

    public bool IsConfigured(ProviderRuntimeConfig cfg) =>
        cfg.Has("PosId", "SecondKey", "ClientId", "ClientSecret");

    public async Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg)
    {
        if (!IsConfigured(cfg)) return new(false, null, "Brak kompletnej konfiguracji PayU.");

        var baseUrl = cfg.Sandbox ? SandboxBase : ProdBase;
        var token = await GetTokenAsync(baseUrl, cfg.Get("ClientId"), cfg.Get("ClientSecret"));
        if (token is null) return new(false, null, "Nie udało się uwierzytelnić w PayU.");

        var order = ctx.Order;
        var amountGrosze = ((long)Math.Round(order.Amount * 100)).ToString();
        var baseApp = ctx.AppBaseUrl.TrimEnd('/');
        var payload = new
        {
            notifyUrl = $"{baseApp}/payments/payu/notify",
            continueUrl = $"{baseApp}/payment/success?order={order.ExtOrderId}",
            customerIp = string.IsNullOrWhiteSpace(ctx.CustomerIp) ? "127.0.0.1" : ctx.CustomerIp,
            merchantPosId = cfg.Get("PosId"),
            description = order.Description ?? ctx.ItemName,
            currencyCode = order.Currency,
            totalAmount = amountGrosze,
            extOrderId = order.ExtOrderId,
            buyer = new { email = ctx.BuyerEmail, language = "pl" },
            products = new[] { new { name = ctx.ItemName, unitPrice = amountGrosze, quantity = "1" } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v2_1/orders");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req); }
        catch (Exception ex) { logger.LogError(ex, "PayU create order failed"); return new(false, null, "Błąd połączenia z PayU."); }

        var body = await resp.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var redirectUri = root.TryGetProperty("redirectUri", out var r) ? r.GetString() : null;
            var payuOrderId = root.TryGetProperty("orderId", out var o) ? o.GetString() : null;
            if (!string.IsNullOrEmpty(redirectUri))
                return new(true, redirectUri, null, payuOrderId);
        }
        catch (Exception ex) { logger.LogError(ex, "PayU response parse failed: {Body}", body); }

        return new(false, null, "Nie udało się utworzyć płatności w PayU.");
    }

    public Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Get("SecondKey")))
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));

        headers.TryGetValue("OpenPayu-Signature", out var sigHeader);
        var incoming = ParseSignature(sigHeader);
        if (incoming is null)
        {
            logger.LogWarning("PayU notify without signature");
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
        }
        var expected = Md5Hex(rawBody + cfg.Get("SecondKey"));
        if (!string.Equals(incoming, expected, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("PayU notify signature mismatch");
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var ord = doc.RootElement.GetProperty("order");
            var extOrderId = ord.TryGetProperty("extOrderId", out var e) ? e.GetString() : null;
            var status = ord.TryGetProperty("status", out var s) ? s.GetString() : null;
            var outcome = status switch
            {
                "COMPLETED" => PaymentOutcome.Paid,
                "CANCELED" => PaymentOutcome.Canceled,
                _ => PaymentOutcome.Pending
            };
            return Task.FromResult(new ProviderNotifyResult(true, extOrderId, outcome));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PayU notify parse failed");
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
        }
    }

    private async Task<string?> GetTokenAsync(string baseUrl, string clientId, string clientSecret)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pl/standard/user/oauth/authorize");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });
        try
        {
            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        }
        catch (Exception ex) { logger.LogError(ex, "PayU token failed"); return null; }
    }

    private static string? ParseSignature(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("signature", StringComparison.OrdinalIgnoreCase))
                return kv[1];
        }
        return null;
    }

    private static string Md5Hex(string input) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
