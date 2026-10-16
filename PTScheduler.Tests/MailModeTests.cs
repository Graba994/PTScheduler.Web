using System.Text.RegularExpressions;
using FluentAssertions;
using MailKit.Net.Smtp;
using MailKit.Security;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Poczta trenerów: własna skrzynka w instancji zarządzanej (stan zdrowia, komunikaty błędów po ludzku)
/// i to, co Leo mówi w Portalu — kto wysyła przez naszą skrzynkę, limit, błędy własnych skrzynek, zmiany.
/// </summary>
public class MailModeTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Regex Numbers = new(@"\d+(?:[.,]\d+)?");

    /// <summary>Strażnik z Portalu: liczby w tekście zapasowym muszą być w faktach (nazwy trenerów nie — to znaczniki).</summary>
    private static void NumbersComeFromFacts(LeoService.Tip tip)
    {
        var source = string.Join(" ", tip.Facts.Select(f => f.Key + " " + f.Value));
        var allowed = Numbers.Matches(source).Select(m => m.Value).ToHashSet();
        Numbers.Matches(tip.Fallback).Select(m => m.Value).Should().OnlyContain(n => allowed.Contains(n), $"tekst: {tip.Fallback}");
        string.Join(" ", tip.Facts.Values).Should().NotContain("Studio", "nazwy trenerów nie trafiają do AI");
    }

    // ── Stan własnej skrzynki ──

    [Fact]
    public void Own_mailbox_is_failing_only_after_a_fresh_unresolved_error()
    {
        EmailSettings.IsFailing(null, null, Now).Should().BeFalse();
        EmailSettings.IsFailing(Now.AddHours(-2), null, Now).Should().BeTrue();
        EmailSettings.IsFailing(Now.AddHours(-2), Now.AddHours(-1), Now).Should().BeFalse("po błędzie mail znów wyszedł");
        EmailSettings.IsFailing(Now.AddHours(-1), Now.AddHours(-2), Now).Should().BeTrue();
        EmailSettings.IsFailing(Now.AddDays(-4), null, Now).Should().BeFalse("stary błąd bez ponownych prób nie straszy w nieskończoność");
    }

    [Fact]
    public void Smtp_errors_are_explained_in_polish()
    {
        SmtpEmailService.Friendly(new AuthenticationException("535 5.7.8 bad credentials"))
            .Should().Contain("hasła aplikacji").And.Contain("535");
        SmtpEmailService.Friendly(new System.Net.Sockets.SocketException(11001))
            .Should().StartWith("Brak połączenia z serwerem");
        SmtpEmailService.Friendly(new SmtpCommandException(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 not owned"))
            .Should().Contain("adresu nadawcy");
        SmtpEmailService.Friendly(new NotSupportedException("The SMTP server does not support authentication."))
            .Should().StartWith("Serwer nie przyjmuje logowania");
        SmtpEmailService.Friendly(new InvalidOperationException("coś innego")).Should().Contain("coś innego");
    }

    // ── Leo o poczcie ──

    private static LeoService.MailTenant T(int id, string? mode, int sent = 0, DateTime? errorAt = null, int? fallbacks = null) =>
        new(id, $"Studio {id}", mode, errorAt, fallbacks, sent);

    [Fact]
    public void Leo_says_who_sends_through_our_mailbox_without_names_in_facts()
    {
        var tips = LeoService.MailTips([T(1, "platform", 40), T(2, "platform", 96), T(3, "own"), T(4, "platform", 0)], [], 300, Now);

        var status = tips.Should().ContainSingle(t => t.Kind == LeoService.MailStatusKind).Subject;
        status.Fallback.Should().Be("Przez naszą skrzynkę wysyła 3 trenerów: dziś 136 maili, najwięcej [trener] (96).");
        status.Placeholders.Should().Contain("[trener]", "Studio 2");
        status.IsTodo.Should().BeFalse("to informacja, nie sprawa do załatwienia");
        NumbersComeFromFacts(status);
    }

    [Fact]
    public void Single_trainer_on_our_mailbox_is_named()
    {
        var status = LeoService.MailTips([T(7, "platform", 1), T(8, "own")], [], 300, Now).Single(t => t.Kind == LeoService.MailStatusKind);
        status.Fallback.Should().Be("Z naszej skrzynki korzysta [trener] — dziś 1 mail.");
        status.Placeholders!["[trener]"].Should().Be("Studio 7");
    }

    [Fact]
    public void Failing_own_mailbox_is_a_todo_with_fallback_count()
    {
        var tips = LeoService.MailTips([T(5, "own", 4, Now.AddHours(-3), 4)], [], 300, Now);

        var tip = tips.Should().ContainSingle(t => t.Kind == "poczta_blad").Subject;
        tip.IsTodo.Should().BeTrue();
        tip.Fallback.Should().Contain("awaryjnie przez naszą skrzynkę (już 4)");
        tip.ActionHref.Should().Be("/panel/tenants/5");
        NumbersComeFromFacts(tip);
    }

    [Fact]
    public void Old_own_mailbox_error_is_ignored()
    {
        LeoService.MailTips([T(5, "own", 0, Now.AddDays(-5), 2)], [], 300, Now).Should().NotContain(t => t.Kind == "poczta_blad");
    }

    [Fact]
    public void Near_daily_limit_suggests_own_mailbox_and_exhausted_limit_is_urgent()
    {
        var near = LeoService.MailTips([T(1, "platform", 250), T(2, "platform", 10)], [], 300, Now).Single(t => t.Kind == "poczta_limit");
        near.Mood.Should().Be("think");
        near.Fallback.Should().StartWith("[trener]: dziś już 250 z 300 maili").And.Contain("własną skrzynkę");
        NumbersComeFromFacts(near);

        var max = LeoService.MailTips([T(1, "platform", 300), T(2, "platform", 260)], [], 300, Now).Single(t => t.Kind == "poczta_limit");
        max.IsTodo.Should().BeTrue();
        max.Fallback.Should().Contain("wyczerpany").And.Contain("Blisko limitu: 2 trenerów");
        NumbersComeFromFacts(max);
    }

    [Fact]
    public void Switching_to_own_mailbox_is_celebrated()
    {
        var tips = LeoService.MailTips([T(3, "own")], [new(3, "own", Now.AddDays(-1))], 300, Now);
        var tip = tips.Should().ContainSingle(t => t.Kind == "poczta_zmiana").Subject;
        tip.IsPraise.Should().BeTrue();
        tip.Fallback.Should().StartWith("[trener] wysyła już maile z własnej skrzynki");
    }

    [Fact]
    public void No_trainers_means_no_mail_tips()
    {
        LeoService.MailTips([], [], 300, Now).Should().BeEmpty();
        LeoService.MailTips([T(1, "own"), T(2, "none")], [], 300, Now).Should().BeEmpty();
    }

    [Fact]
    public void Dashboard_puppy_uses_its_own_ids()
    {
        var ids = Regex.Matches(PuppyArt.AnimatedSvg("leoc"), "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        ids.Should().NotBeEmpty().And.OnlyContain(i => i.StartsWith("leoc"));
        PuppyArt.AnimatedSvg("leoc").Should().NotContain("#leoa");
    }
}
