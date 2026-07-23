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
        var tenants = scope.ServiceProvider.GetRequiredService<TenantService>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
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
                await tenants.SuspendAsync(t.Id);
                t.BillingStatus = "trial_expired";
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to suspend expired trial for {Slug}", t.Slug);
            }
        }
    }
}
