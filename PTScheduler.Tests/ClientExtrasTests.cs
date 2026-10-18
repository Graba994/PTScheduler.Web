using System.Text;
using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Finance;
using PTScheduler.Application.Packages;
using PTScheduler.Application.Scheduling;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>„Kup ponownie”, „Powiadom mnie o wolnym terminie” i „Paczka dla księgowej” — same reguły.</summary>
public class ClientExtrasTests
{
    private static PackageOfferDto Offer(int id, string name, int type, int count, bool pair = false) =>
        new() { Id = id, Name = name, SessionTypeId = type, SessionsCount = count, IsForPair = pair, Price = count * 100 };

    [Fact]
    public void Rebuy_prefers_same_name_and_count_then_count_then_name()
    {
        var offers = new[] { Offer(1, "Start", 1, 4), Offer(2, "Mocny miesiąc", 1, 8), Offer(3, "Pakiet 8", 1, 8), Offer(4, "Duet", 1, 8, pair: true) };
        RebuyOffer.Match(offers, "Pakiet 8", 1, 8, false)!.Id.Should().Be(3);
        RebuyOffer.Match(offers, "stary pakiet", 1, 4, false)!.Id.Should().Be(1);
        RebuyOffer.Match(offers, " start ", 1, 10, false)!.Id.Should().Be(1);
        RebuyOffer.Match(offers, "Duet", 1, 8, true)!.Id.Should().Be(4);
    }

    [Fact]
    public void Rebuy_gives_up_when_nothing_is_clearly_the_same()
    {
        var offers = new[] { Offer(1, "Start", 1, 4), Offer(2, "Mocny", 1, 8), Offer(3, "Online", 2, 4) };
        RebuyOffer.Match(offers, "Inny", 1, 12, false).Should().BeNull();
        RebuyOffer.Match(offers, "Inny", 2, 12, false)!.Id.Should().Be(3); // jedyna oferta tego rodzaju
        RebuyOffer.Match(offers, "Start", 9, 4, false).Should().BeNull();
    }

    [Fact]
    public void SlotWatch_matches_day_and_hours()
    {
        var weekdays = 0b0011111;
        var tuesday9 = new DateTime(2026, 10, 20, 9, 0, 0);
        SlotWatchRules.Matches(weekdays, new(7, 0), new(10, 0), tuesday9, 60).Should().BeTrue();
        SlotWatchRules.Matches(weekdays, new(7, 0), new(10, 0), tuesday9, 90).Should().BeFalse(); // kończy się po 10:00
        SlotWatchRules.Matches(weekdays, new(7, 0), new(10, 0), tuesday9.AddDays(4), 60).Should().BeFalse(); // sobota
        SlotWatchRules.Matches(SlotWatchRules.Bit(DayOfWeek.Sunday), new(7, 0), new(23, 30), new DateTime(2026, 10, 25, 23, 0, 0), 60).Should().BeFalse(); // przez północ
    }

    [Fact]
    public void SlotWatch_window_and_throttle()
    {
        var now = new DateTime(2026, 10, 18, 8, 0, 0);
        SlotWatchRules.InWindow(now.AddHours(1), now).Should().BeFalse();
        SlotWatchRules.InWindow(now.AddHours(3), now).Should().BeTrue();
        SlotWatchRules.InWindow(now.AddDays(15), now).Should().BeFalse();
        var utc = new DateTime(2026, 10, 18, 6, 0, 0, DateTimeKind.Utc);
        SlotWatchRules.CanNotify(null, utc).Should().BeTrue();
        SlotWatchRules.CanNotify(utc.AddHours(-3), utc).Should().BeFalse();
        SlotWatchRules.CanNotify(utc.AddHours(-13), utc).Should().BeTrue();
    }

    [Fact]
    public void SlotWatch_describes_choice()
    {
        SlotWatchRules.Describe(0b0011111, new(7, 0), new(10, 0)).Should().Be("Pn–Pt 7:00–10:00");
        SlotWatchRules.Describe(0b1111111, new(17, 30), new(21, 0)).Should().Be("Codziennie 17:30–21:00");
        SlotWatchRules.Describe(0b0010101, new(6, 0), new(8, 0)).Should().Be("Pn, Śr, Pt 6:00–8:00");
    }

    [Fact]
    public void Accountant_csv_sums_received_without_unpaid_and_escapes_cells()
    {
        var pack = new AccountantPack.Pack(2026, 9, "Studio", [
            new(new DateTime(2026, 9, 3), AccountantPack.Online, "Pakiet", "Pakiet 8", "Anna; Nowak", 800m, "PLN", "PayU", "FV/1/2026", "1234567890"),
            new(new DateTime(2026, 9, 5), AccountantPack.AtTrainer, "Trening", "=HYPERLINK(\"x\")", "Jan", 120m, "PLN", "gotówka"),
            new(new DateTime(2026, 9, 7), AccountantPack.Unpaid, "Pakiet", "Pakiet 4", "Ola", 400m, "PLN", "—")
        ]);
        pack.Received.Should().Be(920m);
        pack.Invoices.Should().Be(1);
        pack.FileStem.Should().Be("ksiegowa-2026-09");

        var bytes = AccountantPack.Csv(pack);
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("\"Anna; Nowak\"");
        text.Should().Contain("800,00");
        text.Should().Contain("\"'=HYPERLINK(\"\"x\"\")\""); // bez formuł w Excelu
        text.Should().Contain("Wpływy razem;;;;;920,00;PLN");
    }

    [Fact]
    public void Accountant_previous_month_crosses_year()
    {
        AccountantPack.PreviousMonth(new DateOnly(2026, 1, 3)).Should().Be((2025, 12));
        AccountantPack.PreviousMonth(new DateOnly(2026, 10, 18)).Should().Be((2026, 9));
        AccountantPack.MonthLabel(2026, 9).Should().Be("wrzesień 2026");
    }
}
