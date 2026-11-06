using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co 10 minut: wizyty, których trener nie oznaczył, stają się „ukończone” 2 godziny po końcu.
/// Patrzy tylko 3 dni wstecz — stare, nigdy nie oznaczone wizyty zostają bez zmian.
/// </summary>
public sealed class SessionAutoCompleteService(IServiceScopeFactory scopeFactory, IAppClock clock, ILogger<SessionAutoCompleteService> logger) : BackgroundService
{
    private static readonly TimeSpan Grace = TimeSpan.FromHours(2);
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(3);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var done = await scope.ServiceProvider.GetRequiredService<ISessionService>().AutoCompletePastAsync(clock.LocalNow, Grace, Lookback);
                if (done > 0) logger.LogInformation("Automatycznie ukończono {Count} wizyt.", done);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Automatyczne kończenie wizyt nie powiodło się."); }
            await Task.Delay(TimeSpan.FromMinutes(10), ct);
        }
    }
}
