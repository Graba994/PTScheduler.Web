using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using Stripe.Checkout;

namespace PTScheduler.Portal.Services;

public class StorePaymentService(
    SiteSettingsService settings,
    IDbContextFactory<PortalDbContext> dbFactory,
    CreditService creditService,
    ILogger<StorePaymentService> logger)
{
    public async Task<List<string>> GetAvailableGatewaysAsync()
    {
        var gateways = new List<string>();

        var stripeKey = await settings.GetAsync(SiteSettingsService.Keys.StripeSecretKey);
        if (!string.IsNullOrWhiteSpace(stripeKey))
            gateways.Add("stripe");

        var payuPosId = await settings.GetAsync(SiteSettingsService.Keys.PayuPosId);
        var payuSecret = await settings.GetAsync(SiteSettingsService.Keys.PayuClientSecret);
        if (!string.IsNullOrWhiteSpace(payuPosId) && !string.IsNullOrWhiteSpace(payuSecret))
            gateways.Add("payu");

        var p24MerchantId = await settings.GetAsync(SiteSettingsService.Keys.P24MerchantId);
        var p24ApiKey = await settings.GetAsync(SiteSettingsService.Keys.P24ApiKey);
        if (!string.IsNullOrWhiteSpace(p24MerchantId) && !string.IsNullOrWhiteSpace(p24ApiKey))
            gateways.Add("przelewy24");

        return gateways;
    }

    public async Task<(string? PaymentUrl, string? ExternalId, string? Error)> CreatePaymentAsync(
        string gateway, decimal amount, string description, string orderGroupId,
        string returnUrl, string notifyBaseUrl, string? buyerEmail = null)
    {
        return gateway switch
        {
            "stripe" => await CreateStripePaymentAsync(amount, description, orderGroupId, returnUrl),
            "payu" => await CreatePayuPaymentAsync(amount, description, orderGroupId, returnUrl, notifyBaseUrl, buyerEmail),
            "przelewy24" => await CreateP24PaymentAsync(amount, description, orderGroupId, returnUrl, notifyBaseUrl, buyerEmail),
            _ => (null, null, $"Nieznana bramka płatności: {gateway}")
        };
    }

    public async Task<bool> HandlePaymentConfirmationAsync(string gateway, string externalId)
    {
        await using var db = dbFactory.CreateDbContext();

        var orders = await db.ServiceOrders
            .Where(o => o.PaymentExternalId == externalId && o.PaymentGateway == gateway)
            .ToListAsync();

        if (orders.Count == 0)
        {
            logger.LogWarning("No orders found for {Gateway} payment {ExternalId}", gateway, externalId);
            return false;
        }

        var now = DateTime.UtcNow;
        foreach (var order in orders)
        {
            if (order.Status != ServiceOrderStatus.AwaitingPayment) continue;
            order.Status = ServiceOrderStatus.Pending;
            order.PaidAt = now;
        }

        var firstOrder = orders[0];
        db.PaymentRecords.Add(new PaymentRecord
        {
            TenantId = firstOrder.TenantId,
            ServiceOrderId = firstOrder.Id,
            ExternalPaymentId = externalId,
            Amount = orders.Where(o => o.PaidAt == now).Sum(o => o.Price),
            Currency = "PLN",
            Status = PaymentRecordStatus.Paid,
            Source = gateway,
            Description = $"Zamówienie usług ({orders.Count} poz.)"
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Payment confirmed: {Gateway} {ExternalId}, {Count} orders updated",
            gateway, externalId, orders.Count);

        // Auto-fulfill credit-based orders (SMS packs, CDN packs)
        var paidOrders = orders.Where(o => o.PaidAt == now).ToList();
        var serviceItemIds = paidOrders.Select(o => o.ServiceItemId).Distinct().ToList();
        var serviceItems = await db.ServiceItems.AsNoTracking()
            .Where(s => serviceItemIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        foreach (var order in paidOrders)
        {
            if (serviceItems.TryGetValue(order.ServiceItemId, out var item) && item.FulfillmentType != "manual")
            {
                try { await creditService.FulfillOrderAsync(order, item); }
                catch (Exception ex) { logger.LogError(ex, "Auto-fulfill failed for order {Id}", order.Id); }
            }
        }

        return true;
    }

    private async Task<(string? PaymentUrl, string? ExternalId, string? Error)> CreateStripePaymentAsync(
        decimal amount, string description, string orderGroupId, string returnUrl)
    {
        var key = await settings.GetAsync(SiteSettingsService.Keys.StripeSecretKey);
        if (string.IsNullOrWhiteSpace(key))
            return (null, null, "Stripe nie skonfigurowany.");

        try
        {
            Stripe.StripeConfiguration.ApiKey = key;
            var service = new SessionService();
            var session = await service.CreateAsync(new SessionCreateOptions
            {
                Mode = "payment",
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "pln",
                            UnitAmount = (long)(amount * 100),
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = description
                            }
                        },
                        Quantity = 1
                    }
                ],
                SuccessUrl = $"{returnUrl}?payment=success&gateway=stripe",
                CancelUrl = $"{returnUrl}?payment=cancel",
                Metadata = new Dictionary<string, string>
                {
                    ["orderGroupId"] = orderGroupId,
                    ["source"] = "store"
                }
            });

            return (session.Url, session.Id, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe payment session creation failed");
            return (null, null, ex.Message);
        }
    }

    private async Task<(string? PaymentUrl, string? ExternalId, string? Error)> CreatePayuPaymentAsync(
        decimal amount, string description, string orderGroupId,
        string returnUrl, string notifyBaseUrl, string? buyerEmail)
    {
        var posId = await settings.GetAsync(SiteSettingsService.Keys.PayuPosId);
        var clientSecret = await settings.GetAsync(SiteSettingsService.Keys.PayuClientSecret);
        var sandbox = await settings.GetAsync(SiteSettingsService.Keys.PayuSandbox);

        if (string.IsNullOrWhiteSpace(posId) || string.IsNullOrWhiteSpace(clientSecret))
            return (null, null, "PayU nie skonfigurowane.");

        var baseUrl = sandbox == "true"
            ? "https://secure.snd.payu.com"
            : "https://secure.payu.com";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            var tokenResp = await http.PostAsync($"{baseUrl}/pl/standard/user/oauth/authorize",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = posId,
                    ["client_secret"] = clientSecret
                }));

            if (!tokenResp.IsSuccessStatusCode)
                return (null, null, $"PayU auth failed: HTTP {(int)tokenResp.StatusCode}");

            var tokenJson = await tokenResp.Content.ReadAsStringAsync();
            using var tokenDoc = JsonDocument.Parse(tokenJson);
            var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

            var orderPayload = JsonSerializer.Serialize(new
            {
                notifyUrl = $"{notifyBaseUrl.TrimEnd('/')}/api/webhooks/payu",
                continueUrl = $"{returnUrl}?payment=success&gateway=payu",
                merchantPosId = posId,
                description,
                currencyCode = "PLN",
                totalAmount = ((int)(amount * 100)).ToString(),
                extOrderId = orderGroupId,
                buyer = string.IsNullOrWhiteSpace(buyerEmail) ? null : new { email = buyerEmail },
                products = new[]
                {
                    new
                    {
                        name = description,
                        unitPrice = ((int)(amount * 100)).ToString(),
                        quantity = "1"
                    }
                }
            }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

            using var orderReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v2_1/orders");
            orderReq.Content = new StringContent(orderPayload, Encoding.UTF8, "application/json");
            orderReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var httpNoRedirect = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            httpNoRedirect.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var orderResp = await httpNoRedirect.PostAsync($"{baseUrl}/api/v2_1/orders",
                new StringContent(orderPayload, Encoding.UTF8, "application/json"));

            string respBody = await orderResp.Content.ReadAsStringAsync();

            if (orderResp.StatusCode == System.Net.HttpStatusCode.Found ||
                orderResp.StatusCode == System.Net.HttpStatusCode.Redirect ||
                (int)orderResp.StatusCode == 302)
            {
                using var doc = JsonDocument.Parse(respBody);
                var redirectUri = doc.RootElement.GetProperty("redirectUri").GetString();
                var payuOrderId = doc.RootElement.GetProperty("orderId").GetString();
                return (redirectUri, payuOrderId, null);
            }

            if (orderResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(respBody);
                var root = doc.RootElement;
                if (root.TryGetProperty("redirectUri", out var uri) && root.TryGetProperty("orderId", out var oid))
                    return (uri.GetString(), oid.GetString(), null);
            }

            return (null, null, $"PayU order failed: HTTP {(int)orderResp.StatusCode} — {respBody}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PayU payment creation failed");
            return (null, null, ex.Message);
        }
    }

    private async Task<(string? PaymentUrl, string? ExternalId, string? Error)> CreateP24PaymentAsync(
        decimal amount, string description, string orderGroupId,
        string returnUrl, string notifyBaseUrl, string? buyerEmail)
    {
        var merchantId = await settings.GetAsync(SiteSettingsService.Keys.P24MerchantId);
        var p24PosId = await settings.GetAsync(SiteSettingsService.Keys.P24PosId);
        var apiKey = await settings.GetAsync(SiteSettingsService.Keys.P24ApiKey);
        var crc = await settings.GetAsync(SiteSettingsService.Keys.P24Crc);
        var sandbox = await settings.GetAsync(SiteSettingsService.Keys.P24Sandbox);

        if (string.IsNullOrWhiteSpace(merchantId) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(crc))
            return (null, null, "Przelewy24 nie skonfigurowane.");

        if (string.IsNullOrWhiteSpace(p24PosId))
            p24PosId = merchantId;

        var baseUrl = sandbox == "true"
            ? "https://sandbox.przelewy24.pl"
            : "https://secure.przelewy24.pl";

        var amountInt = (int)(amount * 100);
        var sign = ComputeP24Sign(orderGroupId, int.Parse(merchantId), amountInt, "PLN", crc);

        var payload = JsonSerializer.Serialize(new
        {
            merchantId = int.Parse(merchantId),
            posId = int.Parse(p24PosId),
            sessionId = orderGroupId,
            amount = amountInt,
            currency = "PLN",
            description,
            email = buyerEmail ?? "noreply@ptscheduler.pl",
            country = "PL",
            language = "pl",
            urlReturn = $"{returnUrl}?payment=success&gateway=przelewy24",
            urlStatus = $"{notifyBaseUrl.TrimEnd('/')}/api/webhooks/przelewy24",
            sign
        });

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var authBytes = Encoding.ASCII.GetBytes($"{p24PosId}:{apiKey}");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var resp = await http.PostAsync($"{baseUrl}/api/v1/transaction/register",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (null, null, $"Przelewy24 register failed: HTTP {(int)resp.StatusCode} — {body}");

            using var doc = JsonDocument.Parse(body);
            var data = doc.RootElement.GetProperty("data");
            var token = data.GetProperty("token").GetString();

            var paymentUrl = $"{baseUrl}/trnRequest/{token}";
            return (paymentUrl, token, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "P24 payment creation failed");
            return (null, null, ex.Message);
        }
    }

    public async Task<bool> VerifyPayuNotification(string body, string? signatureHeader)
    {
        var secondKey = await settings.GetAsync(SiteSettingsService.Keys.PayuSecondKey);
        if (string.IsNullOrWhiteSpace(secondKey) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var parts = signatureHeader.Split(';')
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        if (!parts.TryGetValue("signature", out var expectedSig))
            return false;

        var algorithm = parts.GetValueOrDefault("algorithm", "MD5");
        var concat = body + secondKey;

        string computedSig;
        if (algorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase) ||
            algorithm.Equals("SHA-256", StringComparison.OrdinalIgnoreCase))
        {
            computedSig = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(concat)));
        }
        else
        {
            computedSig = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(concat)));
        }

        return computedSig == expectedSig.ToLowerInvariant();
    }

    public async Task<bool> VerifyP24Notification(JsonElement root)
    {
        var crc = await settings.GetAsync(SiteSettingsService.Keys.P24Crc);
        var merchantId = await settings.GetAsync(SiteSettingsService.Keys.P24MerchantId);
        if (string.IsNullOrWhiteSpace(crc) || string.IsNullOrWhiteSpace(merchantId))
            return false;

        var sessionId = root.GetProperty("sessionId").GetString() ?? "";
        var orderId = root.GetProperty("orderId").GetInt64();
        var amount = root.GetProperty("amount").GetInt32();
        var currency = root.GetProperty("currency").GetString() ?? "PLN";
        var sign = root.GetProperty("sign").GetString() ?? "";

        var expected = ComputeP24Sign(sessionId, orderId, amount, currency, crc);
        return expected == sign;
    }

    public async Task<bool> ConfirmP24Transaction(JsonElement root)
    {
        var merchantId = await settings.GetAsync(SiteSettingsService.Keys.P24MerchantId);
        var p24PosId = await settings.GetAsync(SiteSettingsService.Keys.P24PosId);
        var apiKey = await settings.GetAsync(SiteSettingsService.Keys.P24ApiKey);
        var crc = await settings.GetAsync(SiteSettingsService.Keys.P24Crc);
        var sandbox = await settings.GetAsync(SiteSettingsService.Keys.P24Sandbox);

        if (string.IsNullOrWhiteSpace(merchantId) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(crc))
            return false;

        if (string.IsNullOrWhiteSpace(p24PosId))
            p24PosId = merchantId;

        var baseUrl = sandbox == "true"
            ? "https://sandbox.przelewy24.pl"
            : "https://secure.przelewy24.pl";

        var sessionId = root.GetProperty("sessionId").GetString() ?? "";
        var orderId = root.GetProperty("orderId").GetInt64();
        var amount = root.GetProperty("amount").GetInt32();
        var currency = root.GetProperty("currency").GetString() ?? "PLN";

        var verifySign = ComputeP24Sign(sessionId, orderId, amount, currency, crc);

        var payload = JsonSerializer.Serialize(new
        {
            merchantId = int.Parse(merchantId),
            posId = int.Parse(p24PosId),
            sessionId,
            amount,
            currency,
            orderId,
            sign = verifySign
        });

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var authBytes = Encoding.ASCII.GetBytes($"{p24PosId}:{apiKey}");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var resp = await http.PutAsync($"{baseUrl}/api/v1/transaction/verify",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "P24 transaction verification failed");
            return false;
        }
    }

    private static string ComputeP24Sign(string sessionId, long merchantOrOrderId, int amount, string currency, string crc)
    {
        var data = $$"""{"sessionId":"{{sessionId}}","merchantId":{{merchantOrOrderId}},"amount":{{amount}},"currency":"{{currency}}","crc":"{{crc}}"}""";
        return Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes(data)));
    }

    private static string ComputeP24Sign(string sessionId, int merchantId, int amount, string currency, string crc)
    {
        var data = $$"""{"sessionId":"{{sessionId}}","merchantId":{{merchantId}},"amount":{{amount}},"currency":"{{currency}}","crc":"{{crc}}"}""";
        return Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes(data)));
    }
}
