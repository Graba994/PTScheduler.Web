using FluentAssertions;
using PTScheduler.Application.Messaging;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Szkice wiadomości w stylu trenera: szablony, przykłady z poprawek, linki WhatsApp.</summary>
public class MessageTemplatesTests
{
    private static readonly Dictionary<string, string> NoFacts = [];

    [Fact]
    public void Informal_template_greets_by_name_tag()
    {
        var text = MessageTemplates.Build(DraftPurpose.Comeback, new("ty", "czasem", null, null), NoFacts);
        text.Should().StartWith("Cześć [klient]! Dawno się nie widzieliśmy");
    }

    [Fact]
    public void Formal_template_avoids_name_and_gendered_forms()
    {
        var text = MessageTemplates.Build(DraftPurpose.Comeback, new("pan", "czasem", null, null), NoFacts);
        text.Should().StartWith("Dzień dobry!").And.NotContain("[klient]").And.NotContain("Pan").And.NotContain("Pani");
    }

    [Fact]
    public void Custom_greeting_signature_and_no_emoji_are_applied()
    {
        var text = MessageTemplates.Build(DraftPurpose.Congrats, new("ty", "brak", "Hej!", "Trener Janek"),
            new Dictionary<string, string> { ["liczba_treningow"] = "30" });
        text.Should().Be("Hej [klient]! Masz za sobą już 30 treningów — świetna robota! Widać postęp, tak trzymaj\nTrener Janek");
    }

    [Fact]
    public void Package_template_knows_last_entry()
    {
        MessageTemplates.Build(DraftPurpose.Package, new("ty", "czasem", null, null), new Dictionary<string, string> { ["pozostale_wejscia"] = "1" })
            .Should().Contain("ostatnie wejście");
        MessageTemplates.Build(DraftPurpose.Package, new("ty", "czasem", null, null), new Dictionary<string, string> { ["pozostale_wejscia"] = "0" })
            .Should().Contain("Pakiet właśnie się skończył");
    }

    [Fact]
    public void Example_replaces_client_name_and_contact_details()
    {
        var ex = MessageTemplates.ToExample("Hej Marek! Marku, dzwoń 600 123 456 albo pisz marek@mail.pl", "Marek", "Nowak");
        ex.Should().Be("Hej [klient]! [klient], dzwoń [telefon] albo pisz [e-mail]");
    }

    [Theory]
    [InlineData("Cześć Ania! Dawno się nie widzieliśmy.", "Cześć Ania! Dawno się nie widzieliśmy.", false)]
    [InlineData("Cześć Ania! Dawno się nie widzieliśmy.", "Cześć Ania!  Dawno sie nie widzieliśmy.", false)]
    [InlineData("Cześć Ania! Dawno się nie widzieliśmy.", "Siema Aniu, co tam? Wpadasz w czwartek o 18? Mam luźniejszy grafik 😉", true)]
    public void Learning_only_from_meaningful_edits(string generated, string final, bool expected) =>
        MessageTemplates.IsEdited(generated, final).Should().Be(expected);

    [Theory]
    [InlineData("600 123 456", "48600123456")]
    [InlineData("+48 600-123-456", "48600123456")]
    [InlineData("0048600123456", "48600123456")]
    [InlineData("123", null)]
    [InlineData(null, null)]
    public void WhatsApp_number_is_normalized(string? phone, string? expected) =>
        MessageTemplates.WhatsAppNumber(phone).Should().Be(expected);

    [Fact]
    public void Strip_emoji_keeps_text_clean() =>
        MessageTemplates.StripEmoji("Super 💪 robota 🙂!").Should().Be("Super robota !");
}
