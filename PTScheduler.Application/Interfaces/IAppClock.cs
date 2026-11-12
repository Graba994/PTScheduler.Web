namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Jedyne źródło czasu w aplikacji. Istnieje po to, żeby rozróżnienie
/// między <b>instantem</b> a <b>zegarem ściennym</b> było jawne w typie
/// wywołania, a nie ukryte w tym, jaką strefę ma akurat kontener.
///
/// <para><b>Konwencja obowiązująca w całej aplikacji:</b></para>
/// <list type="bullet">
/// <item><description>
/// <b>Instant</b> — moment w czasie, ten sam dla każdego obserwatora.
/// Znaczniki: <c>CreatedAt</c>, <c>PaidAt</c>, <c>RedeemedAt</c>,
/// <c>Timestamp</c> w audycie. Kolumna <c>timestamp with time zone</c>,
/// <c>DateTimeKind.Utc</c>, źródło: <see cref="UtcNow"/>.
/// </description></item>
/// <item><description>
/// <b>Zegar ścienny</b> — godzina tak, jak widzi ją człowiek w kalendarzu.
/// <c>Session.StartTime</c>, terminy ważności, daty obowiązywania.
/// Kolumna <c>timestamp without time zone</c>,
/// <c>DateTimeKind.Unspecified</c>, źródło: <see cref="LocalNow"/>.
/// </description></item>
/// </list>
///
/// <para>
/// Sesja treningowa o 14:00 to 14:00 na ścianie w studiu — niezależnie od
/// tego, w jakiej strefie działa kontener. Dlatego jest zegarem ściennym,
/// tak samo jak <c>TrainerAvailability</c>, które od początku używa
/// <c>TimeOnly</c>/<c>DateOnly</c>.
/// </para>
///
/// <para>
/// <b>Nigdy nie porównuj wartości z dwóch różnych kategorii.</b>
/// <c>session.StartTime &lt; DateTime.UtcNow</c> jest zawsze błędem —
/// właściwe jest <c>session.StartTime &lt; clock.LocalNow</c>.
/// </para>
/// </summary>
public interface IAppClock
{
    /// <summary>
    /// Bieżący instant w UTC. Do znaczników czasu i wszystkiego, co musi
    /// zachować porządek zdarzeń niezależnie od strefy.
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Bieżąca godzina na zegarze ściennym w strefie aplikacji,
    /// z <c>Kind = Unspecified</c>. Jedyna poprawna wartość do porównywania
    /// z <c>Session.StartTime</c> i innymi polami zegara ściennego.
    /// </summary>
    DateTime LocalNow { get; }

    /// <summary>Dzisiejsza data w strefie aplikacji.</summary>
    DateOnly Today { get; }

    /// <summary>Strefa aplikacji. Konfigurowana przez <c>APP_TIMEZONE</c>.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>
    /// Zegar ścienny → instant. Potrzebne, gdy godzina z kalendarza musi
    /// trafić do systemu operującego na instantach (np. zaproszenie
    /// kalendarzowe albo API zewnętrznej bramki).
    /// </summary>
    /// <remarks>
    /// W godzinie „cofniętej” przy zmianie czasu na zimowy ta sama godzina
    /// ścienna występuje dwa razy; zwracany jest wcześniejszy z instantów.
    /// W godzinie „przeskoczonej” przy zmianie na letni godzina ścienna nie
    /// istnieje — zwracany jest instant po przeskoku.
    /// </remarks>
    DateTime ToUtc(DateTime wallClock);

    /// <summary>Instant → zegar ścienny w strefie aplikacji.</summary>
    DateTime ToWallClock(DateTime utc);
}
