using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

// Wakes every hour and suspends tenants whose trial has ended without
// an active paid subscription. Stripe's own webhooks are the primary
// source of truth for status changes; this is the backstop that also
// covers cases where Stripe never fires a status event (e.g. no card
// on file at trial end).
public class TrialExpirationService(
    IServiceScopeFactory scopeFactory,
    ILogger<TrialExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trial expiration scan failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var tenantSvc = scope.ServiceProvider.GetRequiredService<TenantService>();
        var emailSvc = scope.ServiceProvider.GetRequiredService<EmailService>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;

        var warningThreshold = now.AddDays(3);
        var warnings = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active
                && t.TrialEndsAt != null
                && t.TrialEndsAt > now
                && t.TrialEndsAt <= warningThreshold
                && t.BillingStatus != "active"
                && t.BillingStatus != "past_due"
                && t.BillingStatus != "verified")
            .ToListAsync(ct);

        var alreadyWarned = await db.TenantEvents
            .Where(e => e.EventType == TenantEventTypes.TrialWarning
                && e.OccurredAt > now.AddDays(-2))
            .Select(e => e.TenantId)
            .ToHashSetAsync(ct);

        var plans = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        foreach (var t in warnings.Where(t => !alreadyWarned.Contains(t.Id)))
        {
            var daysLeft = (int)Math.Ceiling((t.TrialEndsAt!.Value - now).TotalDays);
            logger.LogInformation("Trial warning for tenant {Slug} — {Days} days left", t.Slug, daysLeft);

            db.TenantEvents.Add(new TenantEvent
            {
                TenantId = t.Id,
                EventType = TenantEventTypes.TrialWarning,
                Detail = $"{daysLeft} dni do konca"
            });
            await db.SaveChangesAsync(ct);

            if (!string.IsNullOrEmpty(t.StripeSubscriptionId))
            {
                // Karta jest podpięta — przypominamy o pierwszej opłacie i o tym, że można zrezygnować.
                var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == t.PlanId, ct);
                var yearly = t.BillingInterval == "yearly" && PlanPricing.Yearly(t, plan) is > 0;
                var amount = PlanPricing.ForInterval(t, plan);
                _ = emailSvc.SendAsync(t.OwnerEmail, $"Za {daysLeft} dni koniec okresu próbnego — Hantly",
                    emailSvc.TrialEndingWithCardEmailBody(t.OwnerName, daysLeft, amount, yearly, plan?.Name ?? t.PlanId, t.Domain));
                continue;
            }
            var body = emailSvc.TrialWarningEmailBody(t.OwnerName, daysLeft);
            _ = emailSvc.SendAsync(t.OwnerEmail, $"Trial konczy sie za {daysLeft} dni — Hantly", body);
        }

        // Okres próbny wyższego planu (Pro na start albo „Wypróbuj 14 dni”) — 3 dni przed końcem jedno przypomnienie.
        var planTrials = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active && t.TrialPlanId != null && t.TrialPlanEndsAt > now && t.TrialPlanEndsAt <= warningThreshold)
            .ToListAsync(ct);
        var warnedPlanTrial = await db.TenantEvents
            .Where(e => e.EventType == TenantEventTypes.TrialWarning && e.Detail != null && e.Detail.StartsWith("plan:") && e.OccurredAt > now.AddDays(-4))
            .Select(e => e.TenantId).ToHashSetAsync(ct);
        foreach (var t in planTrials.Where(t => !warnedPlanTrial.Contains(t.Id)))
        {
            if (plans.GetValueOrDefault(t.TrialPlanId!) is not { } trialPlan || plans.GetValueOrDefault(t.PlanId) is not { } basePlan) continue;
            var daysLeft = (int)Math.Ceiling((t.TrialPlanEndsAt!.Value - now).TotalDays);
            db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.TrialWarning, Detail = $"plan:{trialPlan.Id}, {daysLeft} dni" });
            await db.SaveChangesAsync(ct);
            _ = emailSvc.SendAsync(t.OwnerEmail, $"Za {daysLeft} dni koniec okresu próbnego {trialPlan.Name} — Hantly",
                emailSvc.PlanTrialEndingEmailBody(t.OwnerName, daysLeft, trialPlan.Name, basePlan.Name, basePlan.MaxClients, basePlan.MonthlyPrice <= 0, t.Domain));
        }

        // Koniec okresu próbnego wyższego planu (np. Pro na start): studio wraca na swój darmowy plan.
        var trialsDone = await db.Tenants
            .Where(t => t.TrialPlanId != null && t.TrialPlanEndsAt != null && t.TrialPlanEndsAt < now && t.Status != TenantStatus.Destroyed)
            .ToListAsync(ct);
        foreach (var t in trialsDone)
        {
            var trialName = plans.GetValueOrDefault(t.TrialPlanId!)?.Name ?? t.TrialPlanId!;
            var baseName = plans.GetValueOrDefault(t.PlanId)?.Name ?? t.PlanId;
            t.TrialPlanId = null;
            t.TrialPlanEndsAt = null;
            db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.TrialExpired, Detail = $"{trialName} → {baseName}" });
            await db.SaveChangesAsync(ct);
            try { await tenantSvc.PushEntitlementsAsync(t.Id); }
            catch (Exception ex) { logger.LogWarning(ex, "Uprawnienia {Slug} po okresie próbnym nie wysłane — studio pobierze je samo.", t.Slug); }
            if (t.Status == TenantStatus.Active)
                _ = emailSvc.SendAsync(t.OwnerEmail, $"Okres próbny {trialName} się skończył — Hantly",
                    emailSvc.PlanTrialEndedEmailBody(t.OwnerName, trialName, baseName, (plans.GetValueOrDefault(t.PlanId)?.MonthlyPrice ?? 0) <= 0, t.Domain));
        }

        // Subskrypcje Stripe (karta podpięta w kreatorze) rozlicza Stripe: po okresie próbnym pobiera opłatę,
        // a nieudana płatność / anulowanie przychodzą webhookiem — tu ich nie zawieszamy.
        var expired = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active
                && t.Plan != null && t.Plan.MonthlyPrice > 0 // darmowy plan nie wygasa
                && t.StripeSubscriptionId == null
                && t.BillingStatus != "verified" // po weryfikacji z kreatora abonament rozliczają rachunki (BillingService)
                && t.TrialEndsAt != null
                && t.TrialEndsAt < now
                && t.BillingStatus != "active"
                && t.BillingStatus != "past_due")
            .ToListAsync(ct);

        foreach (var t in expired)
        {
            if (t.GraceUntil.HasValue && t.GraceUntil.Value > now)
            {
                logger.LogInformation("Trial expired for {Slug} but grace period active until {Until}", t.Slug, t.GraceUntil.Value);
                continue;
            }

            logger.LogInformation("Trial expired for tenant {Slug} — suspending", t.Slug);
            try
            {
                await tenantSvc.SuspendAsync(t.Id);
                t.BillingStatus = "trial_expired";

                db.TenantEvents.Add(new TenantEvent
                {
                    TenantId = t.Id,
                    EventType = TenantEventTypes.TrialExpired
                });

                await db.SaveChangesAsync(ct);

                var body = emailSvc.SuspensionEmailBody(t.OwnerName, "Okres probny zakonczyl sie bez aktywnej subskrypcji.");
                _ = emailSvc.SendAsync(t.OwnerEmail, "Twoje konto Hantly zostalo zawieszone", body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to suspend expired trial for {Slug}", t.Slug);
            }
        }
    }
}
