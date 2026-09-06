using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PTScheduler.Domain.Constants;

namespace PTScheduler.Infrastructure.Services.Payments;

public sealed class KlarnaProvider(ILogger<KlarnaProvider> logger) : IPaymentProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string Key => PaymentProviders.Klarna;

    public bool IsConfigured(ProviderRuntimeConfig cfg) =>
        cfg.Has("Username", "Password");

    public async Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg)
    {
        if (!IsConfigured(cfg)) return new(false, null, "Brak kompletnej konfiguracji Klarna.");

        var baseUrl = GetBaseUrl(cfg);
        var order = ctx.Order;
        var amountMinor = (long)Math.Round(order.Amount * 100);
        var baseApp = ctx.AppBaseUrl.TrimEnd('/');

        var payload = new
        {
            purchase_country = "PL",
            purchase_currency = order.Currency,
            locale = "pl-PL",
            order_amount = amountMinor,
            order_tax_amount = 0,
            order_lines = new[]
            {
                new
                {
                    type = "digital",
                    reference = order.ExtOrderId,
                    name = ctx.ItemName,
                    quantity = 1,
                    unit_price = amountMinor,
                    tax_rate = 0,
                    total_amount = amountMinor,
                    total_tax_amount = 0
                }
            },
            merchant_urls = new
            {
                terms = $"{baseApp}/terms",
                checkout = $"{baseApp}/packages",
                confirmation = $"{baseApp}/payment/success?order={order.ExtOrderId}",
                push = $"{baseApp}/payments/klarna/notify?order={order.ExtOrderId}"
            },
            merchant_reference1 = order.ExtOrderId
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/checkout/v3/orders");
        req.Headers.Authorization = BasicAuth(cfg);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req); }
        catch (Exception ex) { logger.LogError(ex, "Klarna create order failed"); return new(false, null, "Błąd połączenia z Klarna."); }

        var body = await resp.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("redirect_url", out var redir) && redir.GetString() is { } url)
            {
                var klarnaOrderId = root.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;
                return new(true, url, null, klarnaOrderId);
            }

            if (root.TryGetProperty("html_snippet", out var snippet) && snippet.GetString() is { } html)
            {
                var klarnaOrderId = root.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;
                return new(true, $"{baseUrl}/checkout/v3/orders/{klarnaOrderId}", null, klarnaOrderId);
            }

            logger.LogWarning("Klarna no redirect_url in response: {Body}", body);
        }
        catch (Exception ex) { logger.LogError(ex, "Klarna response parse failed: {Body}", body); }

        return new(false, null, "Nie udało się utworzyć zamówienia w Klarna.");
    }

    public async Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg)
    {
        if (!IsConfigured(cfg))
            return new(false, null, PaymentOutcome.Pending);

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var klarnaOrderId = root.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;

            if (string.IsNullOrEmpty(klarnaOrderId))
            {
                logger.LogWarning("Klarna notify missing order_id");
                return new(false, null, PaymentOutcome.Pending);
            }

            var baseUrl = GetBaseUrl(cfg);
            var orderData = await FetchOrderAsync(baseUrl, cfg, klarnaOrderId);
            if (orderData is null)
                return new(false, null, PaymentOutcome.Pending);

            var status = orderData.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            var extOrderId = orderData.RootElement.TryGetProperty("merchant_reference1", out var mr) ? mr.GetString() : null;

            var outcome = status switch
            {
                "checkout_complete" or "AUTHORIZED" => PaymentOutcome.Paid,
                "checkout_incomplete" or "EXPIRED" => PaymentOutcome.Canceled,
                _ => PaymentOutcome.Pending
            };

            if (outcome == PaymentOutcome.Paid)
                await AcknowledgeOrderAsync(baseUrl, cfg, klarnaOrderId);

            return new(true, extOrderId, outcome);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Klarna notify parse failed");
            return new(false, null, PaymentOutcome.Pending);
        }
    }

    private async Task<JsonDocument?> FetchOrderAsync(string baseUrl, ProviderRuntimeConfig cfg, string klarnaOrderId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/checkout/v3/orders/{klarnaOrderId}");
        req.Headers.Authorization = BasicAuth(cfg);
        try
        {
            var resp = await Http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body);
        }
        catch (Exception ex) { logger.LogError(ex, "Klarna fetch order failed"); return null; }
    }

    private async Task AcknowledgeOrderAsync(string baseUrl, ProviderRuntimeConfig cfg, string klarnaOrderId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/ordermanagement/v1/orders/{klarnaOrderId}/acknowledge");
        req.Headers.Authorization = BasicAuth(cfg);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        try { await Http.SendAsync(req); }
        catch (Exception ex) { logger.LogError(ex, "Klarna acknowledge failed"); }
    }

    private static string GetBaseUrl(ProviderRuntimeConfig cfg)
    {
        var region = cfg.Get("Region").ToLowerInvariant();
        if (string.IsNullOrEmpty(region)) region = "eu";

        return (cfg.Sandbox, region) switch
        {
            (true, "eu") => "https://api.playground.klarna.com",
            (true, "na") => "https://api-na.playground.klarna.com",
            (true, "oc") => "https://api-oc.playground.klarna.com",
            (false, "eu") => "https://api.klarna.com",
            (false, "na") => "https://api-na.klarna.com",
            (false, "oc") => "https://api-oc.klarna.com",
            _ => "https://api.playground.klarna.com"
        };
    }

    private static AuthenticationHeaderValue BasicAuth(ProviderRuntimeConfig cfg) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.Get("Username")}:{cfg.Get("Password")}")));
}
