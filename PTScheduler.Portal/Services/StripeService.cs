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

        if (session.Mode == "setup")
        {
            if (tenant.BillingStatus is "none" or "") tenant.BillingStatus = "card";
        }
        else
        {
            tenant.StripeSubscriptionId = session.SubscriptionId;
            if (tenant.BillingStatus is not ("active" or "past_due")) tenant.BillingStatus = "trialing";
        }
        await db.SaveChangesAsync();

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.TrialStarted,
            Detail = $"Checkout session: {session.Id}"
        });
        await db.SaveChangesAsync();

        logger.LogInformation("Stripe checkout completed for tenant {Slug} ({Mode})", tenant.Slug, session.Mode);
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

        // Pozycje dodatków znikają razem z subskrypcją.
        var now = DateTime.UtcNow;
        foreach (var addon in await db.TenantAddons.Where(a => a.TenantId == tenant.Id && a.Status == TenantAddonStatus.Active && a.StripeSubscriptionItemId != null).ToListAsync())
        {
            addon.Status = TenantAddonStatus.Cancelled;
            addon.CancelledAt = now;
        }

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

    /// <summary>
    /// Produkt i ceny planu w Stripe — Portal zakłada je sam z ceny miesięcznej i rocznej planu, więc nie trzeba
    /// kopiować identyfikatorów „price_…”. Ceny w Stripe są niezmienne: po zmianie kwoty powstaje nowa cena.
    /// </summary>
    public async Task<(bool Ok, string? Error)> EnsurePricesAsync(string planId)
    {
        if (await ConfigureAsync() is null) return (false, "Stripe nie jest skonfigurowany.");
        await using var db = dbFactory.CreateDbContext();
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId);
        if (plan is null) return (false, "Nie ma takiego planu.");
        if (plan.MonthlyPrice <= 0) return (true, null);
        try
        {
            if (string.IsNullOrWhiteSpace(plan.StripeProductId))
            {
                var product = await new ProductService().CreateAsync(new ProductCreateOptions
                {
                    Name = $"PTScheduler {plan.Name}",
                    Metadata = new Dictionary<string, string> { ["planId"] = plan.Id }
                });
                plan.StripeProductId = product.Id;
            }
            plan.StripeMonthlyPriceId = await EnsurePriceAsync(plan.StripeProductId, plan.StripeMonthlyPriceId, plan.MonthlyPrice, "month", plan.Currency);
            if (plan.YearlyPrice is > 0)
                plan.StripeYearlyPriceId = await EnsurePriceAsync(plan.StripeProductId, plan.StripeYearlyPriceId, plan.YearlyPrice.Value, "year", plan.Currency);
            await db.SaveChangesAsync();
            return (true, null);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Nie udało się założyć cen planu {Plan} w Stripe.", planId);
            return (false, ex.StripeError?.Message ?? ex.Message);
        }
    }

    private static async Task<string> EnsurePriceAsync(string productId, string? priceId, decimal amount, string interval, string currency)
    {
        var prices = new PriceService();
        var cents = (long)Math.Round(amount * 100);
        if (!string.IsNullOrWhiteSpace(priceId))
        {
            try
            {
                var existing = await prices.GetAsync(priceId);
                if (existing.Active && existing.UnitAmount == cents && existing.Recurring?.Interval == interval
                    && string.Equals(existing.Currency, currency, StringComparison.OrdinalIgnoreCase))
                    return priceId;
            }
            catch (StripeException) { /* ceny nie ma (np. inne konto Stripe) — zakładamy nową */ }
        }
        var created = await prices.CreateAsync(new PriceCreateOptions
        {
            Product = productId,
            UnitAmount = cents,
            Currency = currency.ToLowerInvariant(),
            Recurring = new PriceRecurringOptions { Interval = interval }
        });
        return created.Id;
    }

    /// <summary>
    /// Karta przy publikacji z kreatora — wymagana przy każdym planie. Płatny plan: subskrypcja z okresem
    /// próbnym (pierwsza opłata po jego końcu, miesięcznie albo rocznie), anulowanie w każdej chwili w portalu
    /// klienta. Darmowy plan: tylko zapisanie karty (nic nie pobieramy).
    /// </summary>
    public async Task<(bool Success, string? CheckoutUrl, string? Error)> StartCardCheckoutAsync(
        int tenantId, string successUrl, string cancelUrl, int extraTrialDays = 0)
    {
        if (await ConfigureAsync() is null) return (false, null, "Stripe nie jest skonfigurowany.");

        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant?.Plan is null) return (false, null, "Brak zgłoszenia albo planu.");
        var plan = tenant.Plan;
        var meta = new Dictionary<string, string> { ["tenantId"] = tenant.Id.ToString(), ["slug"] = tenant.Slug };

        try
        {
            if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            {
                var customer = await new CustomerService().CreateAsync(new CustomerCreateOptions
                {
                    Email = tenant.OwnerEmail,
                    Name = string.IsNullOrWhiteSpace(tenant.CompanyName) ? tenant.OwnerName : tenant.CompanyName,
                    Phone = tenant.Phone,
                    Metadata = meta
                });
                tenant.StripeCustomerId = customer.Id;
            }

            SessionCreateOptions options;
            if (plan.MonthlyPrice <= 0)
            {
                options = new SessionCreateOptions
                {
                    Mode = "setup",
                    Customer = tenant.StripeCustomerId,
                    Currency = plan.Currency.ToLowerInvariant(),
                    PaymentMethodTypes = ["card"],
                    SetupIntentData = new SessionSetupIntentDataOptions { Metadata = meta }
                };
            }
            else
            {
                var (pricesOk, pricesError) = await EnsurePricesAsync(plan.Id);
                if (!pricesOk) return (false, null, pricesError);
                await db.Entry(plan).ReloadAsync();
                var priceId = tenant.BillingInterval == "yearly" && !string.IsNullOrWhiteSpace(plan.StripeYearlyPriceId)
                    ? plan.StripeYearlyPriceId : plan.StripeMonthlyPriceId;
                var trialDays = Math.Max(0, plan.TrialDays) + Math.Max(0, extraTrialDays);
                options = new SessionCreateOptions
                {
                    Mode = "subscription",
                    Customer = tenant.StripeCustomerId,
                    PaymentMethodCollection = "always",
                    LineItems = [new() { Price = priceId, Quantity = 1 }],
                    SubscriptionData = new SessionSubscriptionDataOptions
                    {
                        TrialPeriodDays = trialDays > 0 ? trialDays : null,
                        Metadata = meta
                    }
                };
            }
            options.SuccessUrl = successUrl;
            options.CancelUrl = cancelUrl;
            options.Locale = "pl";
            options.Metadata = meta;

            var session = await new SessionService().CreateAsync(options);
            tenant.StripeCheckoutSessionId = session.Id;
            await db.SaveChangesAsync();
            return (true, session.Url, null);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Nie udało się otworzyć płatności kartą dla tenanta {Id}.", tenantId);
            return (false, null, ex.StripeError?.Message ?? ex.Message);
        }
    }

    /// <summary>Powrót z płatności: czy karta naprawdę została podpięta (nie ufamy samemu adresowi powrotu).</summary>
    public async Task<(bool Ok, int? TenantId, string? Error)> VerifyCardCheckoutAsync(string sessionId)
    {
        if (await ConfigureAsync() is null) return (false, null, "Stripe nie jest skonfigurowany.");
        try
        {
            var session = await new SessionService().GetAsync(sessionId);
            await using var db = dbFactory.CreateDbContext();
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.StripeCheckoutSessionId == session.Id);
            if (tenant is null) return (false, null, "Nie znaleziono zgłoszenia dla tej płatności.");
            if (session.Status != "complete") return (false, tenant.Id, "Karta nie została jeszcze dodana.");

            if (session.Mode == "subscription")
            {
                tenant.StripeSubscriptionId = session.SubscriptionId;
                if (tenant.BillingStatus is not ("active" or "past_due")) tenant.BillingStatus = "trialing";
            }
            else if (tenant.BillingStatus is "none" or "")
                tenant.BillingStatus = "card";
            await db.SaveChangesAsync();
            return (true, tenant.Id, null);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Weryfikacja płatności {Session} nie powiodła się.", sessionId);
            return (false, null, ex.StripeError?.Message ?? ex.Message);
        }
    }

    /// <summary>Kwota na saldzie klienta Stripe — odejmie się od kolejnej faktury (np. miesiąc gratis za polecenie).</summary>
    public async Task<bool> AddBalanceCreditAsync(string? customerId, decimal amount, string description)
    {
        if (string.IsNullOrWhiteSpace(customerId) || amount <= 0 || await ConfigureAsync() is null) return false;
        try
        {
            await new Stripe.CustomerBalanceTransactionService().CreateAsync(customerId, new Stripe.CustomerBalanceTransactionCreateOptions
            {
                Amount = -(long)Math.Round(amount * 100m),
                Currency = "pln",
                Description = description
            });
            return true;
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Nie udało się dodać salda w Stripe dla {Customer}.", customerId);
            return false;
        }
    }

    /// <summary>
    /// Okres próbny liczymy od uruchomienia aplikacji, nie od podpięcia karty — jeśli zgłoszenie czekało
    /// w kolejce, przesuwamy koniec okresu próbnego subskrypcji (plan + dodatkowe dni z kodu zaproszenia i polecenia).
    /// </summary>
    public async Task RestartTrialAsync(int tenantId)
    {
        if (await ConfigureAsync() is null) return;
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant?.Plan is null || string.IsNullOrWhiteSpace(tenant.StripeSubscriptionId)) return;
        var days = Math.Max(0, tenant.Plan.TrialDays) + await ReferralService.ExtraTrialDaysAsync(db, tenant);
        if (days == 0) return;
        try
        {
            var subs = new Stripe.SubscriptionService();
            var sub = await subs.GetAsync(tenant.StripeSubscriptionId);
            if (sub.Status != "trialing") return;
            var end = DateTime.UtcNow.AddDays(days);
            if (sub.TrialEnd is { } current && current >= end.AddHours(-1)) return;
            await subs.UpdateAsync(sub.Id, new Stripe.SubscriptionUpdateOptions { TrialEnd = end, ProrationBehavior = "none" });
            tenant.TrialEndsAt = end;
            await db.SaveChangesAsync();
        }
        catch (StripeException ex) { logger.LogWarning(ex, "Nie przesunięto okresu próbnego tenanta {Slug}.", tenant.Slug); }
    }
}
