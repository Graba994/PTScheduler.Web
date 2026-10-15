using System.Xml.Linq;
using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Agent Portalu (Leo): punkty ryzyka odejścia trenera, kategorie wiadomości, czyszczenie danych kontaktowych, grafiki.</summary>
public class LeoTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    private static LeoService.RiskInput Healthy() => new(
        ProvisionedAt: Now.AddDays(-90), ActivatedAt: Now.AddDays(-80), LastActivityAt: Now.AddDays(-1), TrialEndsAt: null,
        BillingStatus: "active", FirstPaidAt: Now.AddDays(-60), CancelRequestedAt: null,
        OverdueBills: 0, OverdueAmount: 0, LowestRecentRating: null, Down: false,
        SessionsLast14: 30, SessionsPrev14: 28, Clients: 12);

    [Fact]
    public void Healthy_trainer_has_no_risk()
    {
        var (score, reasons, _) = LeoService.ScoreRisk(Healthy(), Now);
        score.Should().Be(0);
        reasons.Should().BeEmpty();
    }

    [Fact]
    public void Cancellation_alone_is_high_risk_with_call_suggestion()
    {
        var (score, reasons, suggestion) = LeoService.ScoreRisk(Healthy() with { CancelRequestedAt = Now.AddDays(-2) }, Now);
        score.Should().BeGreaterThanOrEqualTo(LeoService.RiskHigh);
        reasons[0].Should().StartWith("Zgłoszono rezygnację");
        suggestion.Should().Contain("Zadzwoń");
    }

    [Fact]
    public void Inactivity_and_overdue_bill_add_up()
    {
        var (score, reasons, _) = LeoService.ScoreRisk(Healthy() with { LastActivityAt = Now.AddDays(-25), OverdueBills = 1, OverdueAmount = 149 }, Now);
        score.Should().Be(70);
        reasons.Should().HaveCount(2);
        reasons[0].Should().Be("Brak aktywności od 25 dni");
        reasons[1].Should().Contain("1 rachunek po terminie").And.Contain("149 zł");
    }

    [Fact]
    public void Session_drop_counts_only_with_meaningful_history()
    {
        LeoService.ScoreRisk(Healthy() with { SessionsLast14 = 4, SessionsPrev14 = 20 }, Now).Score.Should().Be(25);
        // Za mało treningów wcześniej, żeby mówić o spadku.
        LeoService.ScoreRisk(Healthy() with { SessionsLast14 = 0, SessionsPrev14 = 3 }, Now).Score.Should().Be(0);
    }

    [Fact]
    public void Not_activated_after_a_week_suggests_help_with_activation()
    {
        var input = Healthy() with { ProvisionedAt = Now.AddDays(-9), ActivatedAt = null, Clients = 1 };
        var (score, reasons, suggestion) = LeoService.ScoreRisk(input, Now);
        score.Should().Be(25);
        reasons[0].Should().Be("Bez aktywacji po 9 dniach (1 klient)");
        suggestion.Should().Contain("3 klientów");
    }

    [Fact]
    public void Trial_ending_without_payment_is_flagged()
    {
        var input = Healthy() with { TrialEndsAt = Now.AddDays(3), FirstPaidAt = null, BillingStatus = "trialing" };
        LeoService.ScoreRisk(input, Now).Reasons.Should().Contain("Okres próbny kończy się za 3 dni");
    }

    [Fact]
    public void Score_is_capped_at_100()
    {
        var input = Healthy() with
        {
            CancelRequestedAt = Now, OverdueBills = 2, OverdueAmount = 300, LastActivityAt = Now.AddDays(-30),
            LowestRecentRating = 1, Down = true
        };
        LeoService.ScoreRisk(input, Now).Score.Should().Be(100);
    }

    [Theory]
    [InlineData("Chcę zrezygnować z abonamentu", "rezygnacja")]
    [InlineData("Kalendarz nie działa po aktualizacji", "błąd")]
    [InlineData("Faktura za październik ma złą kwotę", "płatności")]
    [InlineData("Jak podłączyć własną domenę?", "konfiguracja")]
    [InlineData("Fajnie gdyby był import klientów z Excela", "prośba o funkcję")]
    [InlineData("Gdzie zmienić godziny pracy?", "pytanie")]
    [InlineData("Super aplikacja, polecam!", "pochwała")]
    [InlineData("ok", "inne")]
    [InlineData("Niska ocena aplikacji", "inne")]
    [InlineData("Cena jest za wysoka", "płatności")]
    public void Categorize_uses_keywords(string text, string expected) =>
        AssistantService.Categorize(text).Should().Be(expected);

    [Fact]
    public void Scrub_removes_contact_details()
    {
        var text = LeoInsights.Scrub("Napiszcie na anna.kowalska@example.com albo zadzwońcie 600 123 456 lub +48 501-222-333", 200);
        text.Should().NotContain("example.com").And.NotContain("600 123 456").And.NotContain("501-222-333");
        text.Should().Contain("[e-mail]").And.Contain("[telefon]");
    }

    [Fact]
    public void Scrub_keeps_prices_and_short_numbers()
    {
        LeoInsights.Scrub("Zapłaciłbym 99 zł za 30 klientów", 200).Should().Be("Zapłaciłbym 99 zł za 30 klientów");
    }

    [Fact]
    public void Puppy_art_is_valid_svg_in_every_mood()
    {
        foreach (var mood in PuppyArt.Moods)
        {
            var svg = XDocument.Parse(PuppyArt.Svg(mood.Key));
            svg.Root!.Name.LocalName.Should().Be("svg");
            MascotArt.IsMood(mood.Key).Should().BeTrue("Leo używa tych samych kluczy nastrojów co maskotka trenerów");
        }
    }

    [Theory]
    [InlineData(1, "rachunek")]
    [InlineData(3, "rachunki")]
    [InlineData(5, "rachunków")]
    [InlineData(12, "rachunków")]
    [InlineData(22, "rachunki")]
    public void Plural_follows_polish_rules(int n, string expected) =>
        LeoService.Plural(n, "rachunek", "rachunki", "rachunków").Should().Be(expected);
}
