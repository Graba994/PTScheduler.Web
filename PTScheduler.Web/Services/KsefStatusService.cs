using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co 10 minut odświeża stan faktur wysłanych do KSeF (numer KSeF albo powód
/// odrzucenia). Nic nie robi, gdy integracja jest wyłączona.
/// </summary>
public sealed class KsefStatusService(IServiceScopeFactory scopeFactory, ILogger<KsefStatusService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ksef = scope.ServiceProvider.GetRequiredService<IKsefService>();
                var changed = await ksef.RefreshPendingAsync(ct);
                if (changed > 0) logger.LogInformation("KSeF: zaktualizowano status {Count} faktur.", changed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "KSeF: odświeżanie statusów nie powiodło się.");
            }
            await Task.Delay(TimeSpan.FromMinutes(10), ct);
        }
    }
}
