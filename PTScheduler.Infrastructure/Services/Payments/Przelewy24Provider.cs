using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PTScheduler.Domain.Constants;

namespace PTScheduler.Infrastructure.Services.Payments;

public sealed class Przelewy24Provider(ILogger<Przelewy24Provider> logger) : IPaymentProvider
{
    private const string SandboxBase = "https://sandbox.przelewy24.pl";
    private const string ProdBase = "https://secure.przelewy24.pl";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string Key => PaymentProviders.Przelewy24;

    public bool IsConfigured(ProviderRuntimeConfig cfg) =>
        cfg.Has("MerchantId", "PosId", "ApiKey", "CrcKey");

    public async Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg)
    {
        if (!IsConfigured(cfg)) return new(false, null, "Brak kompletnej konfiguracji Przelewy24.");

        var baseUrl = cfg.Sandbox ? SandboxBase : ProdBase;
        var posId = int.Parse(cfg.Get("PosId"));
        var merchantId = int.Parse(cfg.Get("MerchantId"));
        var crcKey = cfg.Get("CrcKey");

        var order = ctx.Order;
        var amountGrosze = (int)Math.Round(order.Amount * 100);
        var baseApp = ctx.AppBaseUrl.TrimEnd('/');
        var sessionId = order.ExtOrderId;

        var signData = JsonSerializer.Serialize(new { sessionId, merchantId, amount = amountGrosze, currency = order.Currency, crc = crcKey });
        var sign = Sha384Hex(signData);

        var payload = new
        {
            merchantId,
            posId,
            sessionId,
            amount = amountGrosze,
            currency = order.Currency,
            description = ctx.ItemName,
            email = ctx.BuyerEmail,
            country = "PL",
            language = "pl",
            urlReturn = $"{baseApp}/payment/success?order={order.ExtOrderId}",
            urlStatus = $"{baseApp}/payments/p24/notify",
            sign
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v2/transaction/register");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{posId}:{cfg.Get("ApiKey")}")));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req); }
        catch (Exception ex) { logger.LogError(ex, "P24 register failed"); return new(false, null, "Błąd połączenia z Przelewy24."); }

        var body = await resp.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var tokenEl))
            {
                var token = tokenEl.GetString();
                if (!string.IsNullOrEmpty(token))
                    return new(true, $"{baseUrl}/trnRequest/{token}", null, token);
            }
            var error = root.TryGetProperty("error", out var err) ? err.GetString() : body;
            logger.LogWarning("P24 register error: {Error}", error);
        }
        catch (Exception ex) { logger.LogError(ex, "P24 response parse failed: {Body}", body); }

        return new(false, null, "Nie udało się zarejestrować transakcji w Przelewy24.");
    }

    public async Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg)
    {
        if (!cfg.Has("MerchantId", "PosId", "CrcKey", "ApiKey"))
            return new(false, null, PaymentOutcome.Pending);

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var merchantId = root.GetProperty("merchantId").GetInt32();
            var posId = root.GetProperty("posId").GetInt32();
            var sessionId = root.GetProperty("sessionId").GetString();
            var amount = root.GetProperty("amount").GetInt32();
            var originAmount = root.GetProperty("originAmount").GetInt32();
            var currency = root.GetProperty("currency").GetString() ?? "PLN";
            var orderId = root.GetProperty("orderId").GetInt32();
            var methodId = root.TryGetProperty("methodId", out var m) ? m.GetInt32() : 0;
            var statement = root.TryGetProperty("statement", out var st) ? st.GetString() ?? "" : "";
            var incomingSign = root.TryGetProperty("sign", out var s) ? s.GetString() : null;

            var crcKey = cfg.Get("CrcKey");
            var signPayload = JsonSerializer.Serialize(new { merchantId, posId, sessionId, amount, originAmount, currency, orderId, methodId, statement, crc = crcKey });
            var expected = Sha384Hex(signPayload);

            if (!string.Equals(incomingSign, expected, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("P24 notify signature mismatch");
                return new(false, null, PaymentOutcome.Pending);
            }

            var verifyResult = await VerifyTransactionAsync(cfg, merchantId, posId, sessionId!, amount, currency, orderId);
            return new(true, sessionId, verifyResult ? PaymentOutcome.Paid : PaymentOutcome.Pending);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "P24 notify parse failed");
            return new(false, null, PaymentOutcome.Pending);
        }
    }

    private async Task<bool> VerifyTransactionAsync(ProviderRuntimeConfig cfg, int merchantId, int posId, string sessionId, int amount, string currency, int orderId)
    {
        var baseUrl = cfg.Sandbox ? SandboxBase : ProdBase;
        var crcKey = cfg.Get("CrcKey");
        var signData = JsonSerializer.Serialize(new { sessionId, orderId, amount, currency, crc = crcKey });
        var sign = Sha384Hex(signData);

        var payload = new { merchantId, posId, sessionId, amount, currency, orderId, sign };

        using var req = new HttpRequestMessage(HttpMethod.Put, $"{baseUrl}/api/v2/transaction/verify");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{posId}:{cfg.Get("ApiKey")}")));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("status", out var status)
                && status.GetString() == "success";
        }
        catch (Exception ex) { logger.LogError(ex, "P24 verify failed"); return false; }
    }

    private static string Sha384Hex(string input) =>
        Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes(input)));
}
