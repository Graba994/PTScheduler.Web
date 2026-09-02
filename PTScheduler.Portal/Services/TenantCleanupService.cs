using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public class TenantCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<TenantCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanInactivityAsync(stoppingToken);
                await ScanSuspendedCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tenant cleanup scan failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ScanInactivityAsync(CancellationToken ct)
    {
        var warningDays = config.GetValue("TenantLifecycle:InactivityWarningDays", 14);
        var suspendDays = config.GetValue("TenantLifecycle:InactivitySuspendDays", 21);

        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var tenantSvc = scope.ServiceProvider.GetRequiredService<TenantService>();
        var emailSvc = scope.ServiceProvider.GetRequiredService<EmailService>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;

        var activeTenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active && t.LastActivityAt != null)
            .ToListAsync(ct);

        var alreadyWarned = await db.TenantEvents
            .Where(e => e.EventType == TenantEventTypes.InactivityWarning
                && e.OccurredAt > now.AddDays(-7))
            .Select(e => e.TenantId)
            .ToHashSetAsync(ct);

        foreach (var t in activeTenants)
        {
            var inactiveDays = (int)(now - t.LastActivityAt!.Value).TotalDays;

            if (inactiveDays >= suspendDays)
            {
                if (t.GraceUntil.HasValue && t.GraceUntil.Value > now)
                {
                    logger.LogInformation("Tenant {Slug} inactive {Days}d but grace period active until {Until}",
                        t.Slug, inactiveDays, t.GraceUntil.Value);
                    continue;
                }

                logger.LogInformation("Suspending inactive tenant {Slug} — {Days} days inactive", t.Slug, inactiveDays);
                try
                {
                    await tenantSvc.SuspendAsync(t.Id);

                    db.TenantEvents.Add(new TenantEvent
                    {
                        TenantId = t.Id,
                        EventType = TenantEventTypes.InactivitySuspended,
                        Detail = $"{inactiveDays} dni nieaktywnosci"
                    });
                    await db.SaveChangesAsync(ct);

                    var body = emailSvc.SuspensionEmailBody(t.OwnerName,
                        $"Twoje konto bylo nieaktywne przez {inactiveDays} dni i zostalo automatycznie zawieszone. Skontaktuj sie z nami, aby je przywrocic.");
                    _ = emailSvc.SendAsync(t.OwnerEmail, "Twoje konto PTScheduler zostalo zawieszone z powodu nieaktywnosci", body);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to suspend inactive tenant {Slug}", t.Slug);
                }
            }
            else if (inactiveDays >= warningDays && !alreadyWarned.Contains(t.Id))
            {
                logger.LogInformation("Inactivity warning for tenant {Slug} — {Days} days inactive", t.Slug, inactiveDays);

                db.TenantEvents.Add(new TenantEvent
                {
                    TenantId = t.Id,
                    EventType = TenantEventTypes.InactivityWarning,
                    Detail = $"{inactiveDays} dni nieaktywnosci"
                });
                await db.SaveChangesAsync(ct);

                var body = emailSvc.InactivityWarningEmailBody(t.OwnerName, inactiveDays, suspendDays - inactiveDays);
                _ = emailSvc.SendAsync(t.OwnerEmail, $"Twoje konto PTScheduler jest nieaktywne od {inactiveDays} dni", body);
            }
        }
    }

    private async Task ScanSuspendedCleanupAsync(CancellationToken ct)
    {
        var warningDays = config.GetValue("TenantLifecycle:SuspendedCleanupWarningDays", 23);
        var deleteDays = config.GetValue("TenantLifecycle:SuspendedCleanupDeleteDays", 30);

        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var tenantSvc = scope.ServiceProvider.GetRequiredService<TenantService>();
        var emailSvc = scope.ServiceProvider.GetRequiredService<EmailService>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;

        var suspendedTenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Suspended)
            .ToListAsync(ct);

        var suspendEvents = await db.TenantEvents
            .Where(e => e.EventType == TenantEventTypes.Suspended
                || e.EventType == TenantEventTypes.InactivitySuspended)
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, SuspendedAt = g.Max(e => e.OccurredAt) })
            .ToDictionaryAsync(x => x.TenantId, x => x.SuspendedAt, ct);

        var alreadyWarned = await db.TenantEvents
            .Where(e => e.EventType == TenantEventTypes.CleanupWarning
                && e.OccurredAt > now.AddDays(-14))
            .Select(e => e.TenantId)
            .ToHashSetAsync(ct);

        foreach (var t in suspendedTenants)
        {
            if (!suspendEvents.TryGetValue(t.Id, out var suspendedAt))
                continue;

            var suspendedDays = (int)(now - suspendedAt).TotalDays;

            if (suspendedDays >= deleteDays)
            {
                if (t.GraceUntil.HasValue && t.GraceUntil.Value > now)
                {
                    logger.LogInformation("Tenant {Slug} suspended {Days}d but grace period active until {Until}",
                        t.Slug, suspendedDays, t.GraceUntil.Value);
                    continue;
                }

                logger.LogInformation("Auto-deleting suspended tenant {Slug} — suspended for {Days} days", t.Slug, suspendedDays);
                try
                {
                    var body = emailSvc.CleanupDeletionEmailBody(t.OwnerName, suspendedDays);
                    _ = emailSvc.SendAsync(t.OwnerEmail, "Twoje konto PTScheduler zostalo trwale usuniete", body);

                    await tenantSvc.DeleteAsync(t.Id, removeContainers: true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to auto-delete suspended tenant {Slug}", t.Slug);
                }
            }
            else if (suspendedDays >= warningDays && !alreadyWarned.Contains(t.Id))
            {
                var daysLeft = deleteDays - suspendedDays;
                logger.LogInformation("Cleanup warning for suspended tenant {Slug} — {DaysLeft} days until deletion", t.Slug, daysLeft);

                db.TenantEvents.Add(new TenantEvent
                {
                    TenantId = t.Id,
                    EventType = TenantEventTypes.CleanupWarning,
                    Detail = $"{daysLeft} dni do usuniecia"
                });
                await db.SaveChangesAsync(ct);

                var body = emailSvc.CleanupWarningEmailBody(t.OwnerName, daysLeft);
                _ = emailSvc.SendAsync(t.OwnerEmail, $"Twoje konto PTScheduler zostanie usuniete za {daysLeft} dni", body);
            }
        }
    }
}
