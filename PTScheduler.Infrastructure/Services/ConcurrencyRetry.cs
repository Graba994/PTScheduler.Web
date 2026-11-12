using Microsoft.EntityFrameworkCore;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Ponawia operację na bazie, gdy optymistyczna współbieżność wykryje konflikt
/// (<see cref="DbUpdateConcurrencyException"/>).
///
/// <para>
/// Używane dla liczników, które wiele ścieżek modyfikuje niezależnie —
/// przede wszystkim <c>SessionPackage.UsedSessions</c>. Każda próba MUSI
/// zaczynać od świeżego odczytu (nowy DbContext w środku operacji), żeby
/// ponowienie zastosowało zmianę na aktualnym stanie, a nie na przeterminowanym.
/// </para>
///
/// <para>
/// To nie zastępuje samego tokena współbieżności — token wykrywa konflikt
/// (zamiast po cichu gubić zapis), a ten helper go gładko obsługuje.
/// </para>
/// </summary>
public static class ConcurrencyRetry
{
    /// <param name="operation">
    /// Operacja do wykonania. Wywoływana od nowa przy każdej próbie, więc
    /// musi sama tworzyć DbContext i odczytywać aktualny stan.
    /// </param>
    /// <param name="maxAttempts">Łączna liczba prób (nie liczba ponowień).</param>
    public static async Task ExecuteAsync(Func<Task> operation, int maxAttempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                // Inny zapis wyprzedził nas. Kolejna próba odczyta świeży stan.
                // Krótka losowa zwłoka rozrzuca kolidujące próby.
                await Task.Delay(Random.Shared.Next(10, 40));
            }
        }
    }
}
