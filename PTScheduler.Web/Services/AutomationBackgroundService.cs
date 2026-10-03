using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co godzinę uruchamia automatyczne wiadomości do klientów (Zarządzanie → Automatyzacje),
/// tylko w ciągu dnia (9:00–20:00 czasu studia), żeby nikt nie dostawał wiadomości w nocy.
/// </summary>
public class AutomationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AutomationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(6), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var hour = scope.ServiceProvider.GetRequiredService<IAppClock>().LocalNow.Hour;
                if (hour is >= 9 and < 20)
                {
                    var sent = await scope.ServiceProvider.GetRequiredService<IAutomationService>()
                        .RunAsync(await ChannelsAsync(scope.ServiceProvider));
                    if (sent > 0) logger.LogInformation("Automatyzacje: wysłano {Count} wiadomości.", sent);
                }
            }
            catch (Exception ex) { logger.LogError(ex, "Automation cycle failed"); }
            await Task.Delay(Interval, ct);
        }
    }

    /// <summary>Kanały dostępne w planie i skonfigurowane (e-mail, push, SMS) — wspólne dla usługi i przycisku „Uruchom teraz”.</summary>
    public static async Task<AutomationChannels> ChannelsAsync(IServiceProvider sp)
    {
        var entitlements = sp.GetRequiredService<EntitlementService>();
        var emailOn = entitlements.IsAllowed("EmailReminders") && await sp.GetRequiredService<IEmailService>().IsEnabledAsync();
        var pushOn = entitlements.IsAllowed("PushNotifications") && (await sp.GetRequiredService<IWebPushService>().GetSettingsAsync()).IsConfigured;
        var smsOn = entitlements.IsAllowed("SmsReminders") && await sp.GetRequiredService<ISmsService>().IsEnabledAsync();
        return new AutomationChannels(emailOn, pushOn, smsOn, entitlements.Limit("MaxSmsPerMonth"), AppUrls.PublicBase() ?? "");
    }
}
