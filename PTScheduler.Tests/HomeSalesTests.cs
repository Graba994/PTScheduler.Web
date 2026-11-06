using FluentAssertions;
using PTScheduler.Portal.Components.Home;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Strona główna: 3 plany na pierwszy rzut oka, krótkie karty i „Wyślij mi link”.</summary>
public class HomeSalesTests
{
    private static readonly Plan Start = new() { Id = "start", Name = "Start", MonthlyPrice = 0, MaxClients = 10, MaxTrainers = 1, EmailReminders = true, SortOrder = 1 };
    private static readonly Plan Starter = new() { Id = "starter", Name = "Starter", MonthlyPrice = 49, MaxClients = 30, MaxTrainers = 1, EmailReminders = true, PaymentsEnabled = true, SortOrder = 2 };
    private static readonly Plan Pro = new() { Id = "pro", Name = "Pro", MonthlyPrice = 99, MaxClients = 9999, MaxTrainers = 1, EmailReminders = true, PaymentsEnabled = true, TrainingPlansEnabled = true, IsFeatured = true, SortOrder = 3 };
    private static readonly Plan Business = new() { Id = "business", Name = "Business", MonthlyPrice = 199, MaxClients = 9999, MaxTrainers = 5, EmailReminders = true, PaymentsEnabled = true, TrainingPlansEnabled = true, FinancialReports = true, SortOrder = 4 };

    [Fact]
    public void Shows_free_featured_and_top_plan_and_keeps_order()
    {
        HomeShared.MainPlans([Start, Starter, Pro, Business]).Select(p => p.Name).Should().Equal("Start", "Pro", "Business");
        HomeShared.MainPlans([Start, Pro]).Should().HaveCount(2);
    }

    [Fact]
    public void Higher_plan_lists_only_what_it_adds()
    {
        var (lead, points) = HomeShared.Highlights(Pro, Start);
        lead.Should().Be("Wszystko z planu Start, a także:");
        points.Should().Contain("Płatności online").And.Contain("Nielimitowani klienci")
            .And.NotContain("Przypomnienia e-mail");
        HomeShared.Highlights(Start, null).Points.Should().HaveCountLessThanOrEqualTo(5);
    }

    [Fact]
    public void For_whom_reads_from_plan_limits()
    {
        HomeShared.ForWhom(Start).Should().StartWith("Na start");
        HomeShared.ForWhom(Pro).Should().Contain("płatności online").And.Contain("bez limitu klientów");
        HomeShared.ForWhom(Business).Should().StartWith("Studio z zespołem: do 5 trenerów");
        HomeShared.ForWhom(new Plan { Name = "Pro2", MonthlyPrice = 99, MaxClients = 50, MaxTrainers = 2, PaymentsEnabled = true })
            .Should().Be("Trener z pełnym grafikiem: płatności online, do 50 klientów, do 2 trenerów");
    }

    [Theory]
    [InlineData("ola@studio.pl", true)]
    [InlineData("Ola <ola@studio.pl>", false)]
    [InlineData("ola@studio", false)]
    [InlineData("", false)]
    public void Email_check(string value, bool ok) => HomeLinks.IsEmail(value).Should().Be(ok);

    [Fact]
    public void Register_link_carries_name_and_safe_color_only()
    {
        HomeLinks.RegisterLink("https://hantly.pl/", "Kasia & Fit", "violet")
            .Should().Be("https://hantly.pl/register?nazwa=Kasia%20%26%20Fit&kolor=violet&utm_source=link-email");
        HomeLinks.RegisterLink("https://hantly.pl", null, "\"><script>").Should().Be("https://hantly.pl/register?utm_source=link-email");
        HomeLinks.Clean("  Studio\u0007 Siła  ", 40).Should().Be("Studio Siła");
    }
}
