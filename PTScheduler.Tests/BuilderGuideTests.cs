using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Asystent w kreatorze aplikacji (Hantly): co mówi na każdym kroku i jak reaguje na wpisane dane.</summary>
public class BuilderGuideTests
{
    private static BuilderGuide.State S(int step = 1, string company = "", string? first = null, bool nameOk = false, string slug = "",
        string? spec = null, int offers = 2, string? free = null, decimal firstPrice = 150, bool pkg = true, int pkgCount = 8, decimal pkgPrice = 1040,
        bool emailFilled = false, bool emailValid = false, int pass = 0, bool consent = false, string? error = null,
        BuilderGuide.Phase phase = BuilderGuide.Phase.Build, bool admin = false, bool domain = false) =>
        new(phase, step, admin, "Hantly", company, first, nameOk, "annafit.hantly.pl", slug, domain,
            spec, "Ocean", "Personal", true, offers, free, firstPrice, pkg, pkgCount, pkgPrice,
            emailFilled, emailValid, pass, "Start", consent, error, "anna@example.com", "Uruchamiam aplikację");

    [Fact]
    public void Greets_and_asks_for_studio_name_first()
    {
        var line = BuilderGuide.Say(S());
        line.Mood.Should().Be("hello");
        line.Text.Should().StartWith("Cześć! Jestem Hantly").And.Contain("nazwy studia");
    }

    [Fact]
    public void Reacts_to_name_and_free_address_then_asks_for_full_name()
    {
        BuilderGuide.Say(S(company: "Anna Fit", slug: "free")).Text
            .Should().Contain("„Anna Fit”").And.Contain("annafit.hantly.pl jest wolny").And.Contain("imię i nazwisko");
        BuilderGuide.Say(S(company: "Anna Fit", first: "Anna", nameOk: true)).Text.Should().StartWith("Miło Cię poznać, Anna!");
    }

    [Fact]
    public void Offer_step_explains_package_saving_and_warns_about_free_or_expensive_package()
    {
        BuilderGuide.Say(S(step: 4)).Text.Replace('\u00A0', ' ').Replace('\u202F', ' ').Should().Contain("Pakiet 8 treningów za 1 040 zł to 13% taniej");
        BuilderGuide.Say(S(step: 4, free: "Trening personalny")).Mood.Should().Be("remind");
        BuilderGuide.Say(S(step: 4, free: "Konsultacja i pomiary")).Mood.Should().NotBe("remind", "darmowa konsultacja to celowy wybór");
        BuilderGuide.Say(S(step: 4, pkgPrice: 1400)).Text.Should().Contain("drożej niż pojedynczy");
        BuilderGuide.Say(S(step: 4, pkg: false)).Text.Should().Contain("pakiet treningów to płatność z góry");
    }

    [Fact]
    public void Account_step_walks_through_email_password_and_consent()
    {
        BuilderGuide.Say(S(step: 5)).Text.Should().Contain("Podaj e-mail");
        BuilderGuide.Say(S(step: 5, emailFilled: true)).Text.Should().Contain("niepełny");
        BuilderGuide.Say(S(step: 5, emailFilled: true, emailValid: true, pass: 1)).Mood.Should().Be("remind");
        BuilderGuide.Say(S(step: 5, emailFilled: true, emailValid: true, pass: 3)).Text.Should().Contain("regulamin");
        var ready = BuilderGuide.Say(S(step: 5, emailFilled: true, emailValid: true, pass: 4, consent: true));
        ready.Mood.Should().Be("celebrate");
        ready.Text.Should().Contain("Opublikuj aplikację");
    }

    [Fact]
    public void Follows_the_trainer_after_publishing()
    {
        BuilderGuide.Say(S(phase: BuilderGuide.Phase.Verify)).Text.Should().Contain("anna@example.com");
        BuilderGuide.Say(S(company: "Anna Fit", phase: BuilderGuide.Phase.Deploying)).Text.Should().Contain("Stawiam Anna Fit — teraz: uruchamiam aplikację");
        BuilderGuide.Say(S(company: "Anna Fit", phase: BuilderGuide.Phase.Done)).Mood.Should().Be("celebrate");
    }

    [Fact]
    public void Errors_take_priority_while_filling_in()
    {
        BuilderGuide.Say(S(step: 3, error: "Coś poszło nie tak")).Mood.Should().Be("remind");
    }

    [Fact]
    public void Texts_are_gender_neutral()
    {
        var states = new[]
        {
            S(), S(company: "Anna Fit", slug: "taken"), S(company: "A B", nameOk: true), S(domain: true, company: "Anna Fit"),
            S(step: 2), S(step: 2, spec: "Trening personalny"), S(step: 3, spec: "Joga"), S(step: 4), S(step: 4, offers: 0),
            S(step: 5), S(step: 5, emailFilled: true, emailValid: true, pass: 4, consent: true), S(step: 5, admin: true),
            S(phase: BuilderGuide.Phase.Card), S(phase: BuilderGuide.Phase.Queued), S(phase: BuilderGuide.Phase.Done, admin: true),
        };
        foreach (var text in states.Select(s => BuilderGuide.Say(s).Text))
        {
            text.Should().NotMatchRegex(@"\b\w+(łeś|łaś|łem|łam)\b", $"tekst: {text}");
            text.Should().NotContain("/a").And.NotContain("(a)").And.NotMatchRegex(@"\b(gotowy|gotowa)\b");
        }
    }

    [Fact]
    public void Platform_is_called_hantly()
    {
        ReferralService.PlatformName.Should().Be("Hantly");
    }
}
