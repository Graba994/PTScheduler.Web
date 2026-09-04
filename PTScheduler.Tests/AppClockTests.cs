using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Infrastructure.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Testy zegara aplikacji. Sedno: <c>LocalNow</c> musi zwracać godzinę
/// w strefie studia niezależnie od strefy maszyny, na której działa proces —
/// bo dokładnie to zawodziło, gdy kod używał <c>DateTime.Now</c> w kontenerze
/// pracującym w UTC.
/// </summary>
public class AppClockTests
{
    private static AppClock Create(string? timeZoneId = null)
    {
        var settings = new Dictionary<string, string?>();
        if (timeZoneId is not null) settings["APP_TIMEZONE"] = timeZoneId;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AppClock(config, NullLogger<AppClock>.Instance);
    }

    [Fact]
    public void Defaults_To_Warsaw_When_Not_Configured()
    {
        Create().TimeZone.Id.Should().Be("Europe/Warsaw");
    }

    [Fact]
    public void Uses_Configured_TimeZone()
    {
        Create("Europe/London").TimeZone.Id.Should().Be("Europe/London");
    }

    [Fact]
    public void Unknown_TimeZone_Falls_Back_Without_Throwing()
    {
        // Literówka w konfiguracji nie może przewrócić aplikacji.
        var act = () => Create("Mars/Olympus_Mons");
        act.Should().NotThrow();
        Create("Mars/Olympus_Mons").TimeZone.Should().NotBeNull();
    }

    [Fact]
    public void LocalNow_Has_Unspecified_Kind()
    {
        // Kind musi być Unspecified — Local oznaczałby strefę maszyny,
        // a to jest właśnie ta pomyłka, którą naprawiamy.
        Create().LocalNow.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void UtcNow_Has_Utc_Kind()
    {
        Create().UtcNow.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void LocalNow_Differs_From_UtcNow_By_Zone_Offset()
    {
        var clock = Create();
        var expectedOffset = clock.TimeZone.GetUtcOffset(clock.UtcNow);

        var delta = clock.LocalNow - clock.UtcNow;

        // Tolerancja na upływ czasu między dwoma odczytami.
        delta.Should().BeCloseTo(expectedOffset, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void LocalNow_Is_Independent_Of_Machine_TimeZone()
    {
        // Najważniejszy test w tym pliku: wynik nie może zależeć od tego,
        // czy proces działa w UTC (kontener) czy w CEST (laptop dewelopera).
        var clock = Create("Europe/Warsaw");

        var viaClock = clock.LocalNow;
        var viaTimeZoneInfo = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));

        viaClock.Should().BeCloseTo(viaTimeZoneInfo, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("2026-01-15T12:00:00")] // czas zimowy, UTC+1
    [InlineData("2026-07-15T12:00:00")] // czas letni,  UTC+2
    public void ToUtc_And_Back_Is_Lossless(string wallClockText)
    {
        var clock = Create("Europe/Warsaw");
        var wallClock = DateTime.Parse(wallClockText, System.Globalization.CultureInfo.InvariantCulture);

        clock.ToWallClock(clock.ToUtc(wallClock)).Should().Be(wallClock);
    }

    [Fact]
    public void ToUtc_Applies_Different_Offset_In_Summer_And_Winter()
    {
        var clock = Create("Europe/Warsaw");

        var winter = clock.ToUtc(new DateTime(2026, 1, 15, 12, 0, 0));
        var summer = clock.ToUtc(new DateTime(2026, 7, 15, 12, 0, 0));

        // Ta sama godzina ścienna to inny instant zimą (UTC+1) i latem (UTC+2).
        winter.Hour.Should().Be(11);
        summer.Hour.Should().Be(10);
    }

    [Fact]
    public void ToUtc_Survives_Nonexistent_Hour_At_Dst_Start()
    {
        // 29.03.2026 o 02:00 zegar skacze na 03:00 — 02:30 nie istnieje.
        // ConvertTimeToUtc rzuciłby wyjątkiem; my przesuwamy się za przeskok.
        var clock = Create("Europe/Warsaw");

        var act = () => clock.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0));

        act.Should().NotThrow();
    }

    [Fact]
    public void ToUtc_Resolves_Ambiguous_Hour_At_Dst_End()
    {
        // 25.10.2026 o 03:00 zegar cofa się na 02:00 — 02:30 występuje dwa razy.
        // Wybieramy wcześniejszy instant.
        var clock = Create("Europe/Warsaw");
        var ambiguous = new DateTime(2026, 10, 25, 2, 30, 0);

        var act = () => clock.ToUtc(ambiguous);

        act.Should().NotThrow();
        clock.ToUtc(ambiguous).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Today_Matches_LocalNow_Date()
    {
        var clock = Create();
        clock.Today.Should().Be(DateOnly.FromDateTime(clock.LocalNow));
    }
}
