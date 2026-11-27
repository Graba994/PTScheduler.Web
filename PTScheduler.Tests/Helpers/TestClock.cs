using PTScheduler.Application.Interfaces;

namespace PTScheduler.Tests.Helpers;

/// <summary>
/// Zegar o zamrożonym czasie. Pozwala testować logikę zależną od „teraz”
/// bez zależności od zegara maszyny testowej — a przy okazji sprawdzić
/// zachowanie w konkretnym momencie, np. w dniu wygaśnięcia promocji albo
/// tuż przed progiem 15 minut przy rezerwacji.
/// </summary>
public sealed class TestClock : IAppClock
{
    private readonly DateTime _wallClock;

    private TestClock(DateTime wallClock, TimeZoneInfo timeZone)
    {
        _wallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        TimeZone = timeZone;
    }

    /// <summary>Zegar zatrzymany na podanej godzinie ściennej.</summary>
    public static TestClock AtWallClock(DateTime wallClock, string timeZoneId = "Europe/Warsaw")
        => new(wallClock, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    /// <summary>Zegar zatrzymany o północy podanego dnia.</summary>
    public static TestClock AtDate(int year, int month, int day)
        => AtWallClock(new DateTime(year, month, day));

    public TimeZoneInfo TimeZone { get; }

    public DateTime LocalNow => _wallClock;

    public DateOnly Today => DateOnly.FromDateTime(_wallClock);

    public DateTime UtcNow => ToUtc(_wallClock);

    public DateTime ToUtc(DateTime wallClock)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (TimeZone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        if (TimeZone.IsAmbiguousTime(unspecified))
            return DateTime.SpecifyKind(
                unspecified - TimeZone.GetAmbiguousTimeOffsets(unspecified).Max(),
                DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone);
    }

    public DateTime ToWallClock(DateTime utc)
        => DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone),
            DateTimeKind.Unspecified);
}
