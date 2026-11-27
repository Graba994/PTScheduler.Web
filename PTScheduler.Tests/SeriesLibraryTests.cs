using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class SeriesLibraryTests
{
    [Fact]
    public void Next_series_slot_is_the_coming_weekday_in_warsaw_time()
    {
        // Piątek 27.11.2026, 9:00 w Polsce (8:00 UTC).
        var now = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc);
        var tuesday = new ContentSeries { Day = 2, Time = new TimeOnly(18, 30) };
        InstagramService.NextSeriesSlot(tuesday, new TimeOnly(9, 0), now).Should().Be(new DateTime(2026, 12, 1, 17, 30, 0));

        // Ten sam dzień: później dziś — dziś; za mniej niż 30 min — brak (następny piątek jest już poza 7 dniami).
        var friday = new ContentSeries { Day = 5, Time = new TimeOnly(18, 0) };
        InstagramService.NextSeriesSlot(friday, new TimeOnly(9, 0), now).Should().Be(new DateTime(2026, 11, 27, 17, 0, 0));
        var fridayMorning = new ContentSeries { Day = 5, Time = new TimeOnly(9, 15) };
        InstagramService.NextSeriesSlot(fridayMorning, new TimeOnly(9, 0), now).Should().BeNull();

        // Bez godziny — godzina z autopilota.
        InstagramService.NextSeriesSlot(new ContentSeries { Day = 1 }, new TimeOnly(12, 0), now).Should().Be(new DateTime(2026, 11, 30, 11, 0, 0));
    }

    [Fact]
    public void Tags_are_normalized()
    {
        LibraryService.ParseTags("Porada, #Plan;plan , ,Klient").Should().Equal("porada", "plan", "klient");
    }

    [Fact]
    public void Content_type_by_header()
    {
        LibraryService.ContentType([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D]).Should().Be("image/png");
        LibraryService.ContentType("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()).Should().Be("image/webp");
        LibraryService.ContentType([0xFF, 0xD8, 0xFF, 0xE0]).Should().Be("image/jpeg");
    }
}
