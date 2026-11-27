using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co godzinę przyznaje nagrody za polecenia, w których znajomy odbył już
/// pierwszą wizytę. Działa tylko, gdy plan tenanta obejmuje program poleceń.
/// </summary>
public sealed class ReferralRewardService(
    IServiceScopeFactory scopeFactory,
    EntitlementService entitlements,
    ILogger<ReferralRewardService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (entitlements.IsAllowed("ReferralProgram"))
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IReferralService>().ProcessPendingAsync();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Polecenia: przyznawanie nagród nie powiodło się.");
            }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
