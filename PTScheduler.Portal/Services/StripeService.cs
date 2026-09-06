using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using Stripe;
using Stripe.Checkout;

namespace PTScheduler.Portal.Services;

public class InvoiceInfo
{
    public string Id { get; set; } = "";
    public string Number { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Status { get; set; } = "";
    public DateTime Date { get; set; }
    public string? HostedInvoiceUrl { get; set; }
    public string? InvoicePdf { get; set; }
}

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
            case EventTypes.InvoicePaymentSucceeded:
                await HandlePaymentSucceededAsync((Invoice)stripeEvent.Data.Object);
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

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.TrialStarted,
            Detail = $"Checkout session: {session.Id}"
        });
        await db.SaveChangesAsync();

        logger.LogInformation("Stripe checkout completed for tenant {Slug}, awaiting manual approval", tenant.Slug);
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
            try { await tenants.ResumeAsync(tenant.Id); } catch (Exception ex) { logger.LogError(ex, "Płatność wznowiła tenanta {TenantId}, ale automatyczne wznowienie kontenerów się nie powiodło — wymaga ręcznej interwencji.", tenant.Id); }
        }

        await db.SaveChangesAsync();
    }

    private async Task HandleSubscriptionDeletedAsync(Stripe.Subscription sub)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.StripeSubscriptionId == sub.Id);
        if (tenant is null) return;

        tenant.BillingStatus = "canceled";

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.Suspended,
            Detail = "Subskrypcja Stripe anulowana"
        });

        await db.SaveChangesAsync();

        try { await tenants.SuspendAsync(tenant.Id); } catch (Exception ex) { logger.LogError(ex, "Subskrypcja tenanta {TenantId} anulowana, ale automatyczne zawieszenie kontenerów się nie powiodło — wymaga ręcznej interwencji.", tenant.Id); }

        var body = email.SuspensionEmailBody(tenant.OwnerName, "Subskrypcja została anulowana.");
        _ = email.SendAsync(tenant.OwnerEmail, "Twoje konto PTScheduler zostalo zawieszone", body);
    }

    private async Task HandlePaymentFailedAsync(Invoice invoice)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);
        if (tenant is null) return;

        tenant.BillingStatus = "past_due";

        db.PaymentRecords.Add(new Entities.PaymentRecord
        {
            TenantId = tenant.Id,
            StripeInvoiceId = invoice.Id,
            StripePaymentIntentId = null,
            Amount = (invoice.AmountDue) / 100m,
            Currency = (invoice.Currency ?? "pln").ToUpperInvariant(),
            Status = PaymentRecordStatus.Failed,
            Description = invoice.Description ?? $"Faktura {invoice.Number}"
        });

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.PaymentFailed,
            Detail = $"Kwota: {invoice.AmountDue / 100m:0.00} {(invoice.Currency ?? "PLN").ToUpperInvariant()}, faktura: {invoice.Number}"
        });

        await db.SaveChangesAsync();
        logger.LogWarning("Payment failed for tenant {Slug} — customer {Customer}", tenant.Slug, invoice.CustomerId);

        var body = email.PaymentFailedEmailBody(tenant.OwnerName, invoice.AmountDue / 100m, invoice.Number ?? "—");
        _ = email.SendAsync(tenant.OwnerEmail, "Nieudana platnosc — PTScheduler", body);
    }

    private async Task HandlePaymentSucceededAsync(Invoice invoice)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);
        if (tenant is null) return;

        db.PaymentRecords.Add(new Entities.PaymentRecord
        {
            TenantId = tenant.Id,
            StripeInvoiceId = invoice.Id,
            StripePaymentIntentId = null,
            Amount = (invoice.AmountPaid > 0 ? invoice.AmountPaid : invoice.AmountDue) / 100m,
            Currency = (invoice.Currency ?? "pln").ToUpperInvariant(),
            Status = PaymentRecordStatus.Paid,
            Description = invoice.Description ?? $"Faktura {invoice.Number}"
        });

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.PaymentReceived,
            Detail = $"Kwota: {(invoice.AmountPaid > 0 ? invoice.AmountPaid : invoice.AmountDue) / 100m:0.00} {(invoice.Currency ?? "PLN").ToUpperInvariant()}, faktura: {invoice.Number}"
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Payment succeeded for tenant {Slug} — invoice {Number}", tenant.Slug, invoice.Number);

        var amount = (invoice.AmountPaid > 0 ? invoice.AmountPaid : invoice.AmountDue) / 100m;
        var body = email.PaymentReceivedEmailBody(tenant.OwnerName, amount, invoice.Number ?? "—");
        _ = email.SendAsync(tenant.OwnerEmail, "Potwierdzenie platnosci — PTScheduler", body);
    }

    public async Task<List<InvoiceInfo>> ListInvoicesAsync(string customerId, int limit = 12)
    {
        var key = await ConfigureAsync();
        if (key is null || string.IsNullOrWhiteSpace(customerId)) return new();

        try
        {
            var svc = new InvoiceService();
            var list = await svc.ListAsync(new InvoiceListOptions
            {
                Customer = customerId,
                Limit = limit
            });
            return list.Data.Select(i => new InvoiceInfo
            {
                Id = i.Id,
                Number = i.Number ?? "—",
                Amount = (i.AmountPaid > 0 ? i.AmountPaid : i.AmountDue) / 100m,
                Currency = (i.Currency ?? "pln").ToUpperInvariant(),
                Status = i.Status ?? "",
                Date = i.Created,
                HostedInvoiceUrl = i.HostedInvoiceUrl,
                InvoicePdf = i.InvoicePdf
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Listing invoices failed for customer {C}", customerId);
            return new();
        }
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
