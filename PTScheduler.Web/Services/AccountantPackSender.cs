using PTScheduler.Application.Finance;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co miesiąc (1.–5. dnia, od 8:00) wysyła księgowej zestawienie poprzedniego miesiąca — gdy właściciel to włączył.
/// Raz na miesiąc: AccountantLastSentMonth. Przy błędzie poczty spróbuje ponownie w kolejnej godzinie.
/// </summary>
public class AccountantPackSender(IServiceScopeFactory scopeFactory, ILogger<AccountantPackSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(7), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await RunAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Accountant pack cycle failed"); }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }

    private async Task RunAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var clock = sp.GetRequiredService<IAppClock>();
        var now = clock.LocalNow;
        if (now.Day > 5 || now.Hour < 8) return;
        if (!sp.GetRequiredService<EntitlementService>().IsAllowed("FinancialReports")) return;

        var packs = sp.GetRequiredService<AccountantPackService>();
        var settings = await packs.GetSettingsAsync();
        var (year, month) = AccountantPack.PreviousMonth(clock.Today);
        if (!settings.AutoSend || string.IsNullOrEmpty(settings.Email) || settings.LastSentMonth == $"{year}-{month:00}") return;

        var (ok, error) = await packs.SendAsync(year, month, null, settings.Email);
        if (ok)
        {
            await packs.MarkAutoSentAsync(year, month);
            logger.LogInformation("Accountant pack {Year}-{Month} sent.", year, month);
        }
        else logger.LogWarning("Accountant pack {Year}-{Month} not sent: {Error}", year, month, error);
    }
}
