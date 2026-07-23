using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using Stripe;
using Stripe.Checkout;

namespace PTScheduler.Portal.Services;

public class StripeService(
    SiteSettingsService settings,
    IDbContextFactory<PortalDbContext> dbFactory,
    TenantService tenants,
    EmailService email,
    ILogger<StripeService> logger)
{
    public async Task<bool> IsConfiguredAsync()
    {
        var s = await settings.GetAsync(SiteSettingsService.Keys.StripeSecretKey);
        return !string.IsNullOrWhiteSpace(s);
    }

    private async Task<string?> ConfigureAsync()
    {
        var key = await settings.GetAsync(SiteSettingsService.Keys.StripeSecretKey);
        if (string.IsNullOrWhiteSpace(key)) return null;
        StripeConfiguration.ApiKey = key;
        return key;
    }

    // Called from /register when the trainer picks a paid plan. Creates a
    // Stripe Customer + Checkout Session with a subscription line, saves
    // the ids on the tenant, and returns the URL to redirect to.
    public async Task<(bool Success, string? CheckoutUrl, string? Error)> StartSubscriptionCheckoutAsync(
        int tenantId, string interval)
    {
        var key = await ConfigureAsync();
        if (key is null) return (false, null, "Stripe nie skonfigurowany.");

        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return (false, null, "Tenant nie istnieje.");

        var priceId = interval == "yearly"
            ? tenant.Plan?.StripeYearlyPriceId
            : tenant.Plan?.StripeMonthlyPriceId;

        if (string.IsNullOrWhiteSpace(priceId))
            return (false, null, $"Plan '{tenant.PlanId}' nie ma Stripe Price ID ({interval}). Ustaw w /panel/plans.");

        var settingsMap = await settings.GetAllAsync(
            SiteSettingsService.Keys.StripeSuccessUrl,
            SiteSettingsService.Keys.StripeCancelUrl);

        var successUrl = string.IsNullOrWhiteSpace(settingsMap[SiteSettingsService.Keys.StripeSuccessUrl])
            ? "https://your-portal.example.com/register/success?session_id={CHECKOUT_SESSION_ID}"
            : settingsMap[SiteSettingsService.Keys.StripeSuccessUrl];
        var cancelUrl = string.IsNullOrWhiteSpace(settingsMap[SiteSettingsService.Keys.StripeCancelUrl])
            ? "https://your-portal.example.com/register/cancel"
            : settingsMap[SiteSettingsService.Keys.StripeCancelUrl];

        try
        {
            var customerService = new CustomerService();
            string customerId = tenant.StripeCustomerId ?? "";
            if (string.IsNullOrWhiteSpace(customerId))
            {
                var customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = tenant.OwnerEmail,
                    Name = tenant.OwnerName,
                    Metadata = new Dictionary<string, string>
                    {
                        ["tenantId"] = tenant.Id.ToString(),
                        ["slug"] = tenant.Slug
                    }
                });
                customerId = customer.Id;
                tenant.StripeCustomerId = customerId;
            }

            var checkoutService = new SessionService();
            var session = await checkoutService.CreateAsync(new SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                LineItems = new List<SessionLineItemOptions>
                {
                    new() { Price = priceId, Quantity = 1 }
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    TrialPeriodDays = tenant.Plan?.TrialDays > 0 ? tenant.Plan.TrialDays : null,
                    Metadata = new Dictionary<string, string>
                    {
                        ["tenantId"] = tenant.Id.ToString(),
                        ["slug"] = tenant.Slug
                    }
                },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                AllowPromotionCodes = true
            });

            tenant.StripeCheckoutSessionId = session.Id;
            await db.SaveChangesAsync();

            return (true, session.Url, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe checkout session creation failed for tenant {Id}", tenantId);
            return (false, null, ex.Message);
        }
    }

    // Webhook handler — Stripe posts JSON events here. We only care about
    // a handful: checkout completion (activate), subscription updates
    // (sync status/plan/period end), invoice payment failures (grace),
    // and subscription deletion (suspend).
    public async Task<(bool Handled, string? Message)> HandleWebhookAsync(string payload, string signatureHeader)
    {
        var key = await ConfigureAsync();
        if (key is null) return (false, "Stripe nie skonfigurowany.");

        var webhookSecret = await settings.GetAsync(SiteSettingsService.Keys.StripeWebhookSecret);
        if (string.IsNullOrWhiteSpace(webhookSecret))
            return (false, "Brak stripe_webhook_secret w ustawieniach.");

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, webhookSecret);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe signature");
            return (false, $"Signature error: {ex.Message}");
        }

        logger.LogInformation("Stripe webhook: {Type}", stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompletedAsync((Session)stripeEvent.Data.Object);
                break;
            case EventTypes.CustomerSubscriptionUpdated:
            case EventTypes.CustomerSubscriptionCreated:
                await HandleSubscriptionChangeAsync((Stripe.Subscription)stripeEvent.Data.Object);
                break;
            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeletedAsync((Stripe.Subscription)stripeEvent.Data.Object);
                break;
            case EventTypes.InvoicePaymentFailed:
                await HandlePaymentFailedAsync((Invoice)stripeEvent.Data.Object);
                break;
        }

        return (true, stripeEvent.Type);
    }

    private async Task HandleCheckoutCompletedAsync(Session session)
    {
        if (!session.Metadata.TryGetValue("tenantId", out var idStr)
            && session.SubscriptionId is null) return;

        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.StripeCheckoutSessionId == session.Id);
        if (tenant is null && int.TryParse(idStr, out var id))
            tenant = await db.Tenants.FindAsync(id);
        if (tenant is null) return;

        tenant.StripeSubscriptionId = session.SubscriptionId;
        tenant.BillingStatus = "trialing";
        await db.SaveChangesAsync();

        if (tenant.Status == TenantStatus.Pending)
        {
            var (ok, output) = await tenants.ProvisionAsync(tenant.Id);
            if (ok)
            {
                logger.LogInformation("Auto-provisioned tenant {Slug} after Stripe payment", tenant.Slug);
                var body = email.WelcomeEmailBody(
                    tenant.OwnerName, tenant.Domain, tenant.Port.ToString(),
                    tenant.Plan?.Name ?? tenant.PlanId);
                _ = email.SendAsync(tenant.OwnerEmail, "Twoja instancja PTScheduler jest gotowa!", body);
            }
            else
            {
                logger.LogError("Provisioning failed after Stripe payment for {Slug}: {Msg}", tenant.Slug, output);
            }
        }
    }

    private async Task HandleSubscriptionChangeAsync(Stripe.Subscription sub)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.StripeSubscriptionId == sub.Id);
        if (tenant is null) return;

        tenant.BillingStatus = sub.Status ?? "unknown";
        if (sub.TrialEnd.HasValue) tenant.TrialEndsAt = sub.TrialEnd;

        // Come back to life if a past-due tenant has just been paid
        if (tenant.Status == TenantStatus.Suspended && sub.Status == "active")
        {
            try { await tenants.ResumeAsync(tenant.Id); } catch { }
        }

        await db.SaveChangesAsync();
    }

    private async Task HandleSubscriptionDeletedAsync(Stripe.Subscription sub)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.StripeSubscriptionId == sub.Id);
        if (tenant is null) return;

        tenant.BillingStatus = "canceled";
        await db.SaveChangesAsync();

        try { await tenants.SuspendAsync(tenant.Id); } catch { }
    }

    private async Task HandlePaymentFailedAsync(Invoice invoice)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);
        if (tenant is null) return;

        tenant.BillingStatus = "past_due";
        await db.SaveChangesAsync();
        logger.LogWarning("Payment failed for tenant {Slug} — customer {Customer}", tenant.Slug, invoice.CustomerId);
    }

    // Returns a link the trainer can use to update payment methods.
    public async Task<string?> CreateCustomerPortalLinkAsync(string customerId, string returnUrl)
    {
        var key = await ConfigureAsync();
        if (key is null) return null;
        try
        {
            var service = new Stripe.BillingPortal.SessionService();
            var session = await service.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl
            });
            return session.Url;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Customer portal link creation failed");
            return null;
        }
    }
}
