using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services.Payments;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Orchestrates payments across multiple gateways: creates orders, dispatches to the
/// selected <see cref="IPaymentProvider"/>, applies webhook outcomes and fulfils
/// paid orders (course access or session-package credits).
/// </summary>
public class PaymentService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IPaymentSettingsService settingsService,
    ICouponService coupons,
    IEnumerable<IPaymentProvider> providers,
    ILogger<PaymentService> logger) : IPaymentService
{
    private IPaymentProvider? Provider(string key) => providers.FirstOrDefault(p => p.Key == key);

    public async Task<List<PaymentOptionDto>> GetEnabledOptionsAsync()
    {
        var settings = await settingsService.GetAsync();
        if (!settings.Enabled) return [];

        var options = new List<PaymentOptionDto>();
        foreach (var cfg in settings.Providers.Where(p => p.Enabled))
        {
            var meta = PaymentProviderCatalog.Find(cfg.Key);
            if (meta is null) continue;
            options.Add(new PaymentOptionDto(cfg.Key, meta.Name, meta.Icon));
        }
        return options;
    }

    public async Task<PaymentInitResult> StartCourseCheckoutAsync(string userId, int courseId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        if (course is null) return new(false, null, "Kurs nie istnieje.");
        if (course.Price <= 0) return new(false, null, "Kurs nie jest płatny.");

        var now = DateTime.UtcNow;
        var hasAccess = await db.CourseEnrollments.AnyAsync(e => e.ApplicationUserId == userId && e.CourseId == courseId
            && !e.IsRevoked && (e.ExpiresAt == null || e.ExpiresAt > now) && (e.StartsAt == null || e.StartsAt <= now));
        if (hasAccess) return new(false, null, "Masz już dostęp do tego kursu.");

        var order = new Order
        {
            ApplicationUserId = userId,
            Kind = OrderKind.Course,
            CourseId = courseId,
            Amount = course.Price,
            Description = $"Kurs: {course.Title}",
            CreatedAt = now
        };
        await ApplyCouponIfAnyAsync(order, couponCode, "course");
        return await StartAsync(db, order, providerKey, course.Title, appBaseUrl, buyerEmail, customerIp);
    }

    public async Task<PaymentInitResult> StartPackageCheckoutAsync(string userId, int packageOfferId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var offer = await db.PackageOffers.FirstOrDefaultAsync(o => o.Id == packageOfferId);
        if (offer is null) return new(false, null, "Pakiet nie istnieje.");
        if (!offer.IsActive) return new(false, null, "Pakiet nie jest dostępny.");
        if (offer.Price <= 0) return new(false, null, "Pakiet nie jest płatny.");

        var client = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
        if (client is null) return new(false, null, "Twoje konto nie jest powiązane z profilem klienta. Skontaktuj się z trenerem.");

        var order = new Order
        {
            ApplicationUserId = userId,
            Kind = OrderKind.Package,
            PackageOfferId = packageOfferId,
            Amount = offer.Price,
            Description = $"Pakiet: {offer.Name}",
            CreatedAt = DateTime.UtcNow
        };
        await ApplyCouponIfAnyAsync(order, couponCode, "package");
        return await StartAsync(db, order, providerKey, offer.Name, appBaseUrl, buyerEmail, customerIp);
    }

    // Validates the coupon and mutates the order to store the discount +
    // final Amount. The caller decides whether to bubble errors up; we
    // silently ignore bad codes so a stale code doesn't block checkout.
    private async Task ApplyCouponIfAnyAsync(Order order, string? couponCode, string targetType)
    {
        if (string.IsNullOrWhiteSpace(couponCode)) return;
        var result = await coupons.ValidateAsync(couponCode, order.Amount, targetType);
        if (!result.IsValid) return;

        order.OriginalAmount = order.Amount;
        order.DiscountAmount = result.DiscountAmount;
        order.Amount = result.FinalAmount;
        order.CouponId = result.CouponId;
        order.CouponCode = result.Code;
    }

    private async Task<PaymentInitResult> StartAsync(ApplicationDbContext db, Order order, string providerKey,
        string itemName, string appBaseUrl, string buyerEmail, string customerIp)
    {
        var settings = await settingsService.GetAsync();
        if (!settings.Enabled) return new(false, null, "Płatności online są wyłączone.");

        order.Provider = providerKey;
        order.Currency = string.IsNullOrWhiteSpace(settings.Currency) ? "PLN" : settings.Currency;
        order.ExtOrderId = Guid.NewGuid().ToString("N");

        // 100% discount — skip gateway entirely, auto-fulfil.
        if (order.Amount <= 0)
        {
            order.Status = OrderStatus.Paid;
            order.PaidAt = DateTime.UtcNow;
            db.Orders.Add(order);
            await FulfilAsync(db, order);
            await db.SaveChangesAsync();

            if (order.CouponId is int cid && order.OriginalAmount.HasValue && order.DiscountAmount.HasValue)
            {
                try
                {
                    await coupons.RedeemAsync(cid, order.ApplicationUserId, null,
                        order.OriginalAmount.Value, order.DiscountAmount.Value, order.Amount,
                        order.Kind == OrderKind.Course ? "course" : "package",
                        order.CourseId ?? order.PackageOfferId);
                }
                catch (Exception ex) { logger.LogError(ex, "Coupon redemption bookkeeping failed for free order {OrderId}", order.Id); }
            }

            var returnUrl = $"{appBaseUrl.TrimEnd('/')}/payment/success?order={order.ExtOrderId}";
            return new(true, returnUrl, null);
        }

        var providerCfg = settings.Provider(providerKey);
        if (providerCfg is null || !providerCfg.Enabled)
            return new(false, null, "Wybrana bramka płatności jest niedostępna.");

        var provider = Provider(providerKey);
        if (provider is null) return new(false, null, "Nieznana bramka płatności.");

        var runtime = new ProviderRuntimeConfig
        {
            Sandbox = providerCfg.Sandbox,
            Currency = settings.Currency,
            Fields = providerCfg.Fields
        };
        if (!provider.IsConfigured(runtime))
            return new(false, null, "Wybrana bramka nie jest w pełni skonfigurowana.");

        order.Status = OrderStatus.Pending;
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var ctx = new ProviderCheckoutContext(order, itemName, buyerEmail, customerIp, appBaseUrl);
        var result = await provider.CreateCheckoutAsync(ctx, runtime);
        if (!result.Ok || string.IsNullOrEmpty(result.RedirectUrl))
            return new(false, null, result.Error ?? "Nie udało się rozpocząć płatności.");

        if (!string.IsNullOrEmpty(result.ProviderOrderId))
        {
            order.PayUOrderId = result.ProviderOrderId;
            await db.SaveChangesAsync();
        }
        return new(true, result.RedirectUrl, null);
    }

    public async Task<bool> HandleNotifyAsync(string providerKey, string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        var provider = Provider(providerKey);
        if (provider is null) { logger.LogWarning("Notify for unknown provider {Provider}", providerKey); return false; }

        var settings = await settingsService.GetAsync();
        var providerCfg = settings.Provider(providerKey);
        if (providerCfg is null) return false;

        var runtime = new ProviderRuntimeConfig
        {
            Sandbox = providerCfg.Sandbox,
            Currency = settings.Currency,
            Fields = providerCfg.Fields
        };

        var res = await provider.HandleNotifyAsync(rawBody, headers, runtime);
        if (!res.Valid || string.IsNullOrEmpty(res.ExtOrderId)) return false;

        await ApplyOutcomeAsync(res.ExtOrderId, res.Outcome);
        return true;
    }

    public async Task<bool> CompleteSimulatorAsync(string extOrderId, bool paid)
    {
        await using var db = dbFactory.CreateDbContext();
        var order = await db.Orders.FirstOrDefaultAsync(o => o.ExtOrderId == extOrderId && o.Provider == PaymentProviders.Simulator);
        if (order is null) return false;
        await ApplyOutcomeAsync(extOrderId, paid ? PaymentOutcome.Paid : PaymentOutcome.Canceled);
        return true;
    }

    private async Task ApplyOutcomeAsync(string extOrderId, PaymentOutcome outcome)
    {
        await using var db = dbFactory.CreateDbContext();
        var order = await db.Orders.FirstOrDefaultAsync(o => o.ExtOrderId == extOrderId);
        if (order is null) return;

        if (outcome == PaymentOutcome.Paid)
        {
            if (order.Status != OrderStatus.Paid)
            {
                order.Status = OrderStatus.Paid;
                order.PaidAt = DateTime.UtcNow;
                await FulfilAsync(db, order);
                await db.SaveChangesAsync();

                // Record coupon redemption after the order is committed so a rollback
                // wouldn't leave the counter incremented without a paid order behind it.
                if (order.CouponId is int cid && order.OriginalAmount.HasValue && order.DiscountAmount.HasValue)
                {
                    try
                    {
                        await coupons.RedeemAsync(cid, order.ApplicationUserId, null,
                            order.OriginalAmount.Value, order.DiscountAmount.Value, order.Amount,
                            order.Kind == OrderKind.Course ? "course" : "package",
                            order.CourseId ?? order.PackageOfferId);
                    }
                    catch (Exception ex) { logger.LogError(ex, "Coupon redemption bookkeeping failed for order {OrderId}", order.Id); }
                }
            }
        }
        else if (outcome == PaymentOutcome.Canceled)
        {
            if (order.Status == OrderStatus.Pending) { order.Status = OrderStatus.Canceled; await db.SaveChangesAsync(); }
        }
        else if (outcome == PaymentOutcome.Failed)
        {
            if (order.Status == OrderStatus.Pending) { order.Status = OrderStatus.Failed; await db.SaveChangesAsync(); }
        }
    }

    private static async Task FulfilAsync(ApplicationDbContext db, Order order)
    {
        if (order.Kind == OrderKind.Course && order.CourseId is int courseId)
            await GrantCourseAsync(db, order, courseId);
        else if (order.Kind == OrderKind.Package && order.PackageOfferId is int offerId)
            await GrantPackageAsync(db, order, offerId);
    }

    private static async Task GrantCourseAsync(ApplicationDbContext db, Order order, int courseId)
    {
        var now = DateTime.UtcNow;
        var has = await db.CourseEnrollments.AnyAsync(e => e.ApplicationUserId == order.ApplicationUserId && e.CourseId == courseId
            && !e.IsRevoked && (e.ExpiresAt == null || e.ExpiresAt > now));
        if (has) return;

        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == courseId);
        var accessType = course?.DefaultAccessType ?? CourseAccessType.Lifetime;
        DateTime? expires = accessType == CourseAccessType.Lifetime
            ? null
            : (course?.DefaultAccessDays is int d ? now.AddDays(d) : null);

        db.CourseEnrollments.Add(new CourseEnrollment
        {
            CourseId = courseId,
            ApplicationUserId = order.ApplicationUserId,
            AccessType = accessType,
            Source = EnrollmentSource.Purchase,
            GrantedAt = now,
            ExpiresAt = expires,
            Notes = $"Zakup (zamówienie {order.ExtOrderId})"
        });
    }

    private static async Task GrantPackageAsync(ApplicationDbContext db, Order order, int offerId)
    {
        var offer = await db.PackageOffers.FirstOrDefaultAsync(o => o.Id == offerId);
        if (offer is null) return;

        var client = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == order.ApplicationUserId);
        if (client is null) return;

        // Avoid double-granting if a package already references this order.
        var already = await db.SessionPackages.AnyAsync(p => p.PaymentReference == order.ExtOrderId);
        if (already) return;

        var now = DateTime.UtcNow;
        var package = new SessionPackage
        {
            ClientId = client.Id,
            CreatedByUserId = offer.CreatedByUserId,
            Name = offer.Name,
            SessionTypeId = offer.SessionTypeId,
            TotalSessions = offer.SessionsCount,
            PricePerSession = offer.SessionsCount > 0 ? Math.Round(offer.Price / offer.SessionsCount, 2) : offer.Price,
            IsPaid = true,
            PaidAt = now,
            PaymentReference = order.ExtOrderId,
            PurchasedAt = now,
            ExpiresAt = offer.ValidDays is int days ? now.AddDays(days) : null,
            Status = PackageStatus.Active,
            Notes = $"Zakup online (zamówienie {order.ExtOrderId})"
        };
        db.SessionPackages.Add(package);
        await db.SaveChangesAsync();

        // Fill any sessions the client already booked awaiting a package.
        var awaiting = await db.Sessions
            .Where(s => s.ClientId == client.Id && s.SessionTypeId == offer.SessionTypeId && s.Status == SessionStatus.AwaitingPackage)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
        foreach (var s in awaiting)
        {
            if (package.UsedSessions >= package.TotalSessions) break;
            s.PackageId = package.Id;
            s.Status = SessionStatus.Scheduled;
            package.UsedSessions++;
        }
        if (package.UsedSessions >= package.TotalSessions) package.Status = PackageStatus.Depleted;
    }

    public async Task<OrderDto?> GetOrderByExtAsync(string extOrderId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Orders.AsNoTracking()
            .Where(o => o.ExtOrderId == extOrderId)
            .Select(o => new OrderDto
            {
                Id = o.Id,
                ItemTitle = o.Kind == OrderKind.Package
                    ? (o.PackageOffer != null ? o.PackageOffer.Name : "Pakiet")
                    : (o.Course != null ? o.Course.Title : "Kurs"),
                Kind = o.Kind.ToString(),
                Provider = o.Provider,
                Amount = o.Amount,
                Currency = o.Currency,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt,
                PaidAt = o.PaidAt,
                OriginalAmount = o.OriginalAmount,
                DiscountAmount = o.DiscountAmount,
                CouponCode = o.CouponCode
            })
            .FirstOrDefaultAsync();
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
                ItemTitle = o.Kind == OrderKind.Package
                    ? (o.PackageOffer != null ? o.PackageOffer.Name : "Pakiet")
                    : (o.Course != null ? o.Course.Title : "Kurs"),
                Kind = o.Kind.ToString(),
                Provider = o.Provider,
                Amount = o.Amount,
                Currency = o.Currency,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt,
                PaidAt = o.PaidAt,
                OriginalAmount = o.OriginalAmount,
                DiscountAmount = o.DiscountAmount,
                CouponCode = o.CouponCode
            })
            .ToListAsync();
    }
}
