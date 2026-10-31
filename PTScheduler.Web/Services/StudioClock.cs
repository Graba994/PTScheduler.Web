using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Statyczny dostęp do zegara ściennego studia (IAppClock) dla komponentów UI —
/// działa też w inicjalizatorach pól i metodach statycznych, gdzie wstrzyknięcie
/// nie jest możliwe.
///
/// Kontener celowo działa w UTC, więc <c>DateTime.Now</c> w UI dawało czas
/// przesunięty o 1–2 h względem Session.StartTime (zegar ścienny): złe „Dziś”,
/// przeszłe terminy jako wolne, przesunięte okno odwołania. Używaj
/// <see cref="Now"/> / <see cref="Today"/> zamiast DateTime.Now / DateTime.Today.
/// </summary>
public static class StudioClock
{
    private static IAppClock? _clock;

    /// <summary>Podpinane raz przy starcie aplikacji (Program.cs).</summary>
    public static void Use(IAppClock clock) => _clock = clock;

    public static DateTime Now => _clock?.LocalNow ?? DateTime.Now;

    public static DateTime Today => Now.Date;
}
