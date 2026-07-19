using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class PayUService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<PayUService> logger) : IPaymentService
{
    private const string SandboxBase = "https://secure.snd.payu.com";
    private const string ProdBase = "https://secure.payu.com";

    // Shared client; AllowAutoRedirect must be off so we can read PayU's 302 body
    // (create-order returns 302 with redirectUri/orderId in the JSON payload).
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<PaymentInitResult> StartCourseCheckoutAsync(string userId, int courseId, string appBaseUrl, string buyerEmail, string customerIp)
    {
        await using var db = dbFactory.CreateDbContext();
        var settings = await db.PaymentSettings.FirstOrDefaultAsync();
        if (settings is null || !settings.Enabled)
            return new(false, null, "Płatności online są wyłączone.");
        if (string.IsNullOrWhiteSpace(settings.PosId) || string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.ClientSecret) || string.IsNullOrWhiteSpace(settings.SecondKey))
            return new(false, null, "Brak kompletnej konfiguracji PayU.");

        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return new(false, null, "Kurs nie istnieje.");
        if (course.Price <= 0) return new(false, null, "Kurs nie jest płatny.");

        var now = DateTime.UtcNow;
        var hasAccess = await db.CourseEnrollments.AnyAsync(e => e.ApplicationUserId == userId && e.CourseId == courseId
            && !e.IsRevoked && (e.ExpiresAt == null || e.ExpiresAt > now) && (e.StartsAt == null || e.StartsAt <= now));
        if (hasAccess) return new(false, null, "Masz już dostęp do tego kursu.");

        var baseUrl = settings.Sandbox ? SandboxBase : ProdBase;
        var token = await GetTokenAsync(baseUrl, settings.ClientId!, settings.ClientSecret!);
        if (token is null) return new(false, null, "Nie udało się uwierzytelnić w PayU.");

        var order = new Order
        {
            ApplicationUserId = userId,
            CourseId = courseId,
            ExtOrderId = Guid.NewGuid().ToString("N"),
            Amount = course.Price,
            Currency = string.IsNullOrWhiteSpace(settings.Currency) ? "PLN" : settings.Currency,
            Status = OrderStatus.Pending,
            Description = $"Kurs: {course.Title}",
            CreatedAt = now
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var amountGrosze = ((long)Math.Round(course.Price * 100)).ToString();
        var baseApp = appBaseUrl.TrimEnd('/');
        var payload = new
        {
            notifyUrl = $"{baseApp}/payments/payu/notify",
            continueUrl = $"{baseApp}/my/orders",
            customerIp = string.IsNullOrWhiteSpace(customerIp) ? "127.0.0.1" : customerIp,
            merchantPosId = settings.PosId,
            description = order.Description,
            currencyCode = order.Currency,
            totalAmount = amountGrosze,
            extOrderId = order.ExtOrderId,
            buyer = new { email = buyerEmail, language = "pl" },
            products = new[] { new { name = course.Title, unitPrice = amountGrosze, quantity = "1" } }
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
            if (!string.IsNullOrEmpty(payuOrderId))
            {
                order.PayUOrderId = payuOrderId;
                await db.SaveChangesAsync();
            }
            if (!string.IsNullOrEmpty(redirectUri))
                return new(true, redirectUri, null);
        }
        catch (Exception ex) { logger.LogError(ex, "PayU response parse failed: {Body}", body); }

        return new(false, null, "Nie udało się utworzyć płatności w PayU.");
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

    public async Task<bool> HandleNotifyAsync(string rawBody, string? signatureHeader)
    {
        await using var db = dbFactory.CreateDbContext();
        var settings = await db.PaymentSettings.FirstOrDefaultAsync();
        if (settings is null || string.IsNullOrWhiteSpace(settings.SecondKey)) return false;

        var incoming = ParseSignature(signatureHeader);
        if (incoming is null) { logger.LogWarning("PayU notify without signature"); return false; }
        var expected = Md5Hex(rawBody + settings.SecondKey);
        if (!string.Equals(incoming, expected, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("PayU notify signature mismatch");
            return false;
        }

        string? extOrderId, status;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var ord = doc.RootElement.GetProperty("order");
            extOrderId = ord.TryGetProperty("extOrderId", out var e) ? e.GetString() : null;
            status = ord.TryGetProperty("status", out var s) ? s.GetString() : null;
        }
        catch (Exception ex) { logger.LogError(ex, "PayU notify parse failed"); return false; }

        if (string.IsNullOrEmpty(extOrderId)) return false;

        var order = await db.Orders.FirstOrDefaultAsync(o => o.ExtOrderId == extOrderId);
        if (order is null) return false;

        if (status == "COMPLETED")
        {
            if (order.Status != OrderStatus.Paid)
            {
                order.Status = OrderStatus.Paid;
                order.PaidAt = DateTime.UtcNow;
                await GrantAccessAsync(db, order);
                await db.SaveChangesAsync();
            }
        }
        else if (status == "CANCELED")
        {
            order.Status = OrderStatus.Canceled;
            await db.SaveChangesAsync();
        }
        return true;
    }

    public async Task<List<OrderDto>> GetMyOrdersAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Orders.AsNoTracking()
            .Where(o => o.ApplicationUserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDto
            {
                Id = o.Id,
                CourseTitle = o.Course.Title,
                Amount = o.Amount,
                Currency = o.Currency,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt,
                PaidAt = o.PaidAt
            })
            .ToListAsync();
    }

    private static async Task GrantAccessAsync(ApplicationDbContext db, Order order)
    {
        var now = DateTime.UtcNow;
        var has = await db.CourseEnrollments.AnyAsync(e => e.ApplicationUserId == order.ApplicationUserId && e.CourseId == order.CourseId
            && !e.IsRevoked && (e.ExpiresAt == null || e.ExpiresAt > now));
        if (has) return;

        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == order.CourseId);
        var accessType = course?.DefaultAccessType ?? CourseAccessType.Lifetime;
        DateTime? expires = accessType == CourseAccessType.Lifetime
            ? null
            : (course?.DefaultAccessDays is int d ? now.AddDays(d) : null);

        db.CourseEnrollments.Add(new CourseEnrollment
        {
            CourseId = order.CourseId,
            ApplicationUserId = order.ApplicationUserId,
            AccessType = accessType,
            Source = EnrollmentSource.Purchase,
            GrantedAt = now,
            ExpiresAt = expires,
            Notes = $"Zakup (zamówienie {order.ExtOrderId})"
        });
    }

    private static string? ParseSignature(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        // Format: sender=...;signature=xxxx;algorithm=MD5;content=DOCUMENT
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
