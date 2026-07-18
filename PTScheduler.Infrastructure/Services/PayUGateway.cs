using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

public class PayUGateway(
    IHttpClientFactory httpClientFactory,
    ISiteSettingsService siteSettings,
    ILogger<PayUGateway> logger) : IPaymentGateway
{
    private SiteSettingsDto? _cached;

    public string ProviderName => "PayU";

    public bool IsConfigured
    {
        get
        {
            var s = GetSettings();
            return !string.IsNullOrEmpty(s.PayUPosId) && !string.IsNullOrEmpty(s.PayUClientId);
        }
    }

    private string BaseUrl
    {
        get
        {
            var s = GetSettings();
            return s.PayUIsSandbox
                ? "https://secure.snd.payu.com"
                : "https://secure.payu.com";
        }
    }

    private string PosId => GetSettings().PayUPosId ?? "";
    private string SecondKey => GetSettings().PayUSecondKey ?? "";
    private string ClientId => GetSettings().PayUClientId ?? "";
    private string ClientSecret => GetSettings().PayUClientSecret ?? "";

    private SiteSettingsDto GetSettings() =>
        _cached ??= siteSettings.GetAsync().GetAwaiter().GetResult();

    public async Task<PaymentRedirect> CreatePaymentAsync(PaymentRequest request)
    {
        var token = await GetAccessTokenAsync();
        var client = httpClientFactory.CreateClient("PayU");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var amountInCents = (int)(request.Amount * 100);

        var payload = new
        {
            notifyUrl = request.NotifyUrl,
            continueUrl = request.ContinueUrl,
            customerIp = request.CustomerIp,
            merchantPosId = PosId,
            description = request.Description,
            currencyCode = request.Currency,
            totalAmount = amountInCents.ToString(),
            extOrderId = request.OrderId,
            buyer = new
            {
                email = request.CustomerEmail,
                firstName = ExtractFirst(request.CustomerName),
                lastName = ExtractLast(request.CustomerName),
                language = "pl"
            },
            products = new[]
            {
                new
                {
                    name = request.Description,
                    unitPrice = amountInCents.ToString(),
                    quantity = "1"
                }
            }
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2_1/orders")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

        var body = await response.Content.ReadAsStringAsync();
        logger.LogInformation("PayU CreateOrder response {Status}: {Body}",
            (int)response.StatusCode, body);

        // PayU returns 302 with redirectUri in JSON, or 200 with status
        var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        var redirectUri = root.TryGetProperty("redirectUri", out var r) ? r.GetString() : null;
        var orderId = root.TryGetProperty("orderId", out var oid) ? oid.GetString() : null;

        if (string.IsNullOrEmpty(redirectUri))
            throw new InvalidOperationException(
                $"PayU nie zwróciło redirectUri. Status: {root.GetProperty("status").GetProperty("statusCode").GetString()}");

        return new PaymentRedirect
        {
            RedirectUrl = redirectUri,
            ExternalOrderId = orderId ?? ""
        };
    }

    public async Task<PaymentNotification?> ParseNotificationAsync(Stream body, string? signatureHeader)
    {
        var json = await new StreamReader(body, Encoding.UTF8).ReadToEndAsync();
        logger.LogInformation("PayU notification: {Body}", json);

        if (!VerifySignature(json, signatureHeader))
        {
            logger.LogWarning("PayU notification signature verification failed");
            return null;
        }

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var order = root.GetProperty("order");

        return new PaymentNotification
        {
            ExternalOrderId = order.GetProperty("orderId").GetString() ?? "",
            InternalOrderId = order.TryGetProperty("extOrderId", out var ext) ? ext.GetString() ?? "" : "",
            Status = order.GetProperty("status").GetString() ?? ""
        };
    }

    private bool VerifySignature(string body, string? header)
    {
        if (string.IsNullOrEmpty(SecondKey) || string.IsNullOrEmpty(header))
            return false;

        // Header format: "sender=checkout;signature=abc123;algorithm=MD5;content=DOCUMENT"
        var parts = header.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        if (!parts.TryGetValue("signature", out var expectedSignature))
            return false;

        var algorithm = parts.GetValueOrDefault("algorithm", "MD5");
        var concatenated = body + SecondKey;

        string computed;
        if (algorithm.Equals("MD5", StringComparison.OrdinalIgnoreCase))
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(concatenated));
            computed = Convert.ToHexStringLower(hash);
        }
        else if (algorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(concatenated));
            computed = Convert.ToHexStringLower(hash);
        }
        else
        {
            logger.LogWarning("Unsupported PayU signature algorithm: {Algorithm}", algorithm);
            return false;
        }

        return string.Equals(computed, expectedSignature, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var client = httpClientFactory.CreateClient("PayU");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret
        });

        var response = await client.PostAsync($"{BaseUrl}/pl/standard/user/oauth/authorize", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("PayU OAuth: brak access_token w odpowiedzi.");
    }

    private static string? ExtractFirst(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return name.Trim().Split(' ', 2)[0];
    }

    private static string? ExtractLast(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var parts = name.Trim().Split(' ', 2);
        return parts.Length > 1 ? parts[1] : null;
    }
}
