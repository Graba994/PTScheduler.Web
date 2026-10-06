using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Implementacja <see cref="IAppClock"/> oparta o strefę z konfiguracji.
///
/// <para>
/// Strefa pochodzi z <c>APP_TIMEZONE</c> (zmienna środowiskowa lub klucz
/// w konfiguracji); domyślnie <c>Europe/Warsaw</c>. Jest rozwiązywana raz,
/// przy tworzeniu — celowo, bo strefa instancji nie zmienia się w trakcie
/// działania, a jej podmiana w locie unieważniłaby porównania czasowe
/// trwające w tle.
/// </para>
///
/// <para>
/// Klasa jest bezstanowa poza rozwiązaną strefą, więc rejestrujemy ją
/// jako singleton.
/// </para>
/// </summary>
public sealed class AppClock : IAppClock
{
    private const string DefaultTimeZoneId = "Europe/Warsaw";

    public TimeZoneInfo TimeZone { get; }

    public AppClock(IConfiguration config, ILogger<AppClock> logger)
    {
        var configured = config["APP_TIMEZONE"];
        var id = string.IsNullOrWhiteSpace(configured) ? DefaultTimeZoneId : configured;

        TimeZone = Resolve(id, logger)
                   // Gdy skonfigurowana strefa zawiedzie, próbujemy domyślnej —
                   // ale tylko jeśli to nie ona właśnie zawiodła.
                   ?? (id == DefaultTimeZoneId ? null : Resolve(DefaultTimeZoneId, logger))
                   // Ostatnia deska ratunku. Dojdziemy tu, gdy w obrazie brakuje
                   // bazy tzdata. UTC jest zawsze dostępne, bo nie wymaga tzdata.
                   // Godziny będą przesunięte o offset strefy, ale aplikacja
                   // wstanie i zdąży o tym zalogować.
                   ?? FallbackToUtc(logger);
    }

    private static TimeZoneInfo? Resolve(string id, ILogger<AppClock> logger)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Error, nie Warning: cicha zmiana strefy przesuwa wszystkie godziny
            // i jest bardzo trudna do wyśledzenia po fakcie.
            logger.LogError(ex, "Nie udało się rozwiązać strefy czasowej '{TimeZoneId}'.", id);
            return null;
        }
    }

    private static TimeZoneInfo FallbackToUtc(ILogger<AppClock> logger)
    {
        logger.LogError(
            "Żadna strefa czasowa nie została rozwiązana — używam UTC. " +
            "Najczęstsza przyczyna: brak pakietu tzdata w obrazie kontenera. " +
            "Godziny sesji i przypomnień będą przesunięte o offset strefy studia.");
        return TimeZoneInfo.Utc;
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime LocalNow =>
        // Kind = Unspecified, bo to zegar ścienny, nie instant. Kind=Local
        // oznaczałby strefę maszyny, która w kontenerze jest UTC i nie ma
        // nic wspólnego ze strefą tenanta.
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone),
            DateTimeKind.Unspecified);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    public DateTime ToUtc(DateTime wallClock)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        // Godzina przeskoczona przy przejściu na czas letni nie istnieje na
        // ścianie. ConvertTimeToUtc rzuciłby wyjątkiem, więc przesuwamy się
        // za przeskok — sesja umówiona na nieistniejącą godzinę odbędzie się
        // zaraz po zmianie czasu.
        if (TimeZone.IsInvalidTime(unspecified))
        {
            var adjustment = TimeZone.GetAdjustmentRules()
                .FirstOrDefault(r => unspecified >= r.DateStart && unspecified <= r.DateEnd);
            var delta = adjustment?.DaylightDelta ?? TimeSpan.FromHours(1);
            unspecified = unspecified.Add(delta);
        }

        // Godzina cofnięta występuje dwa razy; ConvertTimeToUtc wybiera
        // interpretację standardową (późniejszy instant). Bierzemy wcześniejszy,
        // bo dla terminu w kalendarzu „pierwsze wystąpienie” jest intuicyjne.
        if (TimeZone.IsAmbiguousTime(unspecified))
        {
            var offsets = TimeZone.GetAmbiguousTimeOffsets(unspecified);
            var earliest = offsets.Max(); // większy offset = wcześniejszy instant UTC
            return DateTime.SpecifyKind(unspecified - earliest, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone);
    }

    public DateTime ToWallClock(DateTime utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone),
            DateTimeKind.Unspecified);
}
