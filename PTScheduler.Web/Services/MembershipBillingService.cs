using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Co godzinę rozlicza karnety cykliczne: tworzy nowe okresy (pakiet sesji
/// + należność), oznacza zaległości i wysyła przypomnienia. Operacja jest
/// idempotentna — kolejne przebiegi nie dublują okresów.
/// </summary>
public sealed class MembershipBillingService(IServiceScopeFactory scopeFactory, ILogger<MembershipBillingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(90), ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IMembershipService>();
                var changes = await svc.ProcessBillingAsync(ct);
                if (changes > 0) logger.LogInformation("Karnety: {Count} zmian w rozliczeniach.", changes);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Karnety: rozliczenie nie powiodło się.");
            }
            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}
