using FluentAssertions;
using PTScheduler.Domain.Rules;
using PTScheduler.Web.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Dokumenty dla klientów trenera (dane usługodawcy) i waluta „zł” w interfejsie.</summary>
public class StudioLegalTests
{
    [Fact]
    public void Unregistered_trainer_is_identified_by_name_and_address_without_nip()
    {
        var i = new StudioLegalService.Info("Studio Siła", "Anna Kowalska", "ul. Długa 5, 00-950 Warszawa", null, "anna@studio.pl", false, "", []);
        i.Who.Should().Be("Anna Kowalska, ul. Długa 5, 00-950 Warszawa");
        i.Complete.Should().BeTrue();
        i.Missing().Should().BeEmpty();
    }

    [Fact]
    public void Business_shows_nip_and_missing_data_is_listed()
    {
        new StudioLegalService.Info("S", "Firma Anna", "Adres", "5260250995", "a@b.pl", false, "", []).Who.Should().EndWith("NIP 5260250995");
        var empty = new StudioLegalService.Info("Studio Siła", "", "", null, null, false, "", []);
        empty.Who.Should().Be("właściciel aplikacji Studio Siła");
        empty.Missing().Should().Equal("imię i nazwisko albo nazwa firmy", "adres", "e-mail kontaktowy");
        empty.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("PLN", "zł")]
    [InlineData("pln", "zł")]
    [InlineData(null, "zł")]
    [InlineData("", "zł")]
    [InlineData("EUR", "EUR")]
    public void Currency_label_prefers_zl(string? code, string expected) => CurrencyLabel.Of(code).Should().Be(expected);
}
