using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co minutę sprawdza połączone kalendarze Google i synchronizuje te, którym
/// minął interwał: wizyty → Google, zajęte terminy z Google → blokady grafiku.
/// </summary>
public sealed class CalendarSyncService(
    IServiceScopeFactory scopeFactory,
    ILogger<CalendarSyncService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IGoogleCalendarService>().SyncDueAsync(Interval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Google Calendar: cykl synchronizacji nie powiódł się.");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
