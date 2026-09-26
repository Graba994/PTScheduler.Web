using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Przypomina klientom o kończącym się pakiecie: „zostały Ci 1–2 treningi” oraz
/// „pakiet wygasa za ≤7 dni”, z linkiem do sklepu. Raz na pakiet i rodzaj —
/// trwały znacznik w SessionPackage (LowCreditsNotifiedAt / ExpiryNotifiedAt).
/// Kanały: e-mail (jeśli skonfigurowany i w planie) oraz push. Szanuje moduł
/// „Przypomnienia o pakietach” i preferencję klienta ExpiringPackages.
/// </summary>
public class PackageReminderService(IServiceScopeFactory scopeFactory, ILogger<PackageReminderService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(3);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Po starcie dajemy czas migracjom i pozostałym usługom.
        await Task.Delay(TimeSpan.FromMinutes(5), ct);

        while (!ct.IsCancellationRequested)
        {
            try { await RunCycleAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Package reminder cycle failed"); }
            await Task.Delay(Interval, ct);
        }
    }

    private enum Kind { LowCredits, Expiring }

    private async Task RunCycleAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var modules = await sp.GetRequiredService<IModuleSettingsService>().GetAsync();
        if (!modules.PackageRemindersEnabled) return;

        var entitlements = sp.GetRequiredService<EntitlementService>();
        var emailService = sp.GetRequiredService<IEmailService>();
        var push = sp.GetRequiredService<IWebPushService>();
        var emailEnabled = entitlements.IsAllowed("EmailReminders") && await emailService.IsEnabledAsync();
        var pushEnabled = entitlements.IsAllowed("PushNotifications") && (await push.GetSettingsAsync()).IsConfigured;
        if (!emailEnabled && !pushEnabled) return; // Nic nie wyślemy — nie zużywamy znaczników.

        var db = sp.GetRequiredService<ApplicationDbContext>();
        var clock = sp.GetRequiredService<IAppClock>();
        var utcNow = clock.UtcNow;
        var expiryCutoff = utcNow.AddDays(AttentionRules.ExpiringDaysClient);

        var candidates = await db.SessionPackages
            .Include(p => p.Client)
            .Where(p => p.Status == PackageStatus.Active
                     && p.Client.Status == ClientStatus.Active
                     && p.TotalSessions > p.UsedSessions
                     && (p.ExpiresAt == null || p.ExpiresAt > utcNow)
                     && ((p.ExpiryNotifiedAt == null && p.ExpiresAt != null && p.ExpiresAt <= expiryCutoff)
                         || (p.LowCreditsNotifiedAt == null && p.UsedSessions > 0
                             && p.TotalSessions - p.UsedSessions <= AttentionRules.LowCreditsThreshold)))
            .ToListAsync();
        if (candidates.Count == 0) return;

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var prefs = sp.GetRequiredService<INotificationPreferencesService>();
        var templates = sp.GetRequiredService<IEmailTemplateService>();
        var hasShop = (await sp.GetRequiredService<IPackageOfferService>().GetActiveOffersAsync()).Count > 0;
        var shopPath = hasShop ? "/packages" : "/my/packages";
        var shopButton = hasShop ? "Kup kolejny pakiet" : "Zobacz swoje pakiety";

        foreach (var pkg in candidates)
        {
            // Wygaśnięcie jest pilniejsze — jeśli dotyczy obu, wysyłamy tylko je w tym cyklu.
            var kind = pkg.ExpiryNotifiedAt == null && pkg.ExpiresAt is { } exp && exp <= expiryCutoff
                ? Kind.Expiring
                : Kind.LowCredits;
            try
            {
                var userId = pkg.Client.ApplicationUserId;
                if (!await prefs.IsEnabledAsync(userId, NotificationTypes.ExpiringPackages))
                {
                    MarkHandled(pkg, kind, utcNow); // Klient zrezygnował — nie wracamy do tego pakietu.
                    await db.SaveChangesAsync();
                    continue;
                }

                var user = await userManager.FindByIdAsync(userId);
                var trainer = string.IsNullOrEmpty(pkg.Client.TrainerUserId) ? null : await userManager.FindByIdAsync(pkg.Client.TrainerUserId);
                var trainerName = trainer is null ? "" : $"{trainer.FirstName} {trainer.LastName}".Trim();
                var remaining = pkg.TotalSessions - pkg.UsedSessions;
                var remainingText = AttentionRules.Trainings(remaining);
                var expiresText = pkg.ExpiresAt is { } e ? clock.ToWallClock(e).ToString("dd.MM.yyyy") : "";
                var sent = false;

                if (emailEnabled && !string.IsNullOrEmpty(user?.Email))
                {
                    try
                    {
                        var vars = new Dictionary<string, string>
                        {
                            ["ClientName"] = pkg.Client.FirstName,
                            ["PackageName"] = pkg.Name,
                            ["Remaining"] = remainingText,
                            ["ExpiresAt"] = expiresText,
                            ["ShopLink"] = AppUrls.Absolute(shopPath),
                            ["ShopButton"] = shopButton,
                            ["TrainerName"] = string.IsNullOrEmpty(trainerName) ? "Twój trener" : trainerName
                        };
                        var key = kind == Kind.Expiring ? "package-expiring" : "package-low-credits";
                        var (subject, html) = await templates.RenderAsync(key, vars);
                        await emailService.SendAsync(user.Email, $"{pkg.Client.FirstName} {pkg.Client.LastName}".Trim(), subject, html);
                        sent = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Package {Id}: reminder email failed.", pkg.Id);
                    }
                }

                if (pushEnabled)
                {
                    try
                    {
                        await push.SendAsync(userId, new PushMessageDto
                        {
                            Title = kind == Kind.Expiring
                                ? $"Pakiet wygasa {expiresText}"
                                : $"Zostały Ci {remainingText}",
                            Body = kind == Kind.Expiring
                                ? $"Zostały w nim {remainingText} — umów je albo przedłuż pakiet."
                                : "Zadbaj o kolejny pakiet, żeby nie wypaść z rytmu 💪",
                            Url = shopPath
                        });
                        sent = true;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Package {Id}: reminder push failed.", pkg.Id);
                    }
                }

                // Znacznik tylko po faktycznej wysyłce — przy awarii obu kanałów spróbujemy w kolejnym cyklu.
                if (sent)
                {
                    MarkHandled(pkg, kind, utcNow);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Package {Id}: {Kind} reminder sent to client {ClientId}.", pkg.Id, kind, pkg.ClientId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Package {Id}: reminder processing failed.", pkg.Id);
            }
        }
    }

    private static void MarkHandled(PTScheduler.Domain.Entities.SessionPackage pkg, Kind kind, DateTime utcNow)
    {
        if (kind == Kind.Expiring) pkg.ExpiryNotifiedAt = utcNow;
        else pkg.LowCreditsNotifiedAt = utcNow;
    }
}
