using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co minutę zwalnia terminy zarezerwowane z płatnością online, których nikt nie opłacił
/// w 15 minut — żeby porzucona płatność nie blokowała grafiku trenera.
/// </summary>
public sealed class OffPackageHoldService(IServiceScopeFactory scopeFactory, ILogger<OffPackageHoldService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var expired = await scope.ServiceProvider.GetRequiredService<IOffPackageService>().ExpireHoldsAsync();
                if (expired > 0) logger.LogInformation("Zwolniono {Count} nieopłaconych rezerwacji online.", expired);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Zwalnianie nieopłaconych rezerwacji nie powiodło się."); }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
