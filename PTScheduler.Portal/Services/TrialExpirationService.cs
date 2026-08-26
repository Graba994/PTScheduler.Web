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
                && t.BillingStatus != "past_due")
            .ToListAsync(ct);

        var alreadyWarned = await db.TenantEvents
            .Where(e => e.EventType == TenantEventTypes.TrialWarning
                && e.OccurredAt > now.AddDays(-2))
            .Select(e => e.TenantId)
            .ToHashSetAsync(ct);

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

            var body = emailSvc.TrialWarningEmailBody(t.OwnerName, daysLeft);
            _ = emailSvc.SendAsync(t.OwnerEmail, $"Trial konczy sie za {daysLeft} dni — PTScheduler", body);
        }

        var expired = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active
                && t.TrialEndsAt != null
                && t.TrialEndsAt < now
                && t.BillingStatus != "active"
                && t.BillingStatus != "past_due")
            .ToListAsync(ct);

        foreach (var t in expired)
        {
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
                _ = emailSvc.SendAsync(t.OwnerEmail, "Twoje konto PTScheduler zostalo zawieszone", body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to suspend expired trial for {Slug}", t.Slug);
            }
        }
    }
}
