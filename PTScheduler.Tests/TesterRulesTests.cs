using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Program testerów: tygodnie, kiedy czeka ankieta i ile tygodni bez opinii.</summary>
public class TesterRulesTests
{
    private static readonly DateTime Since = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static Tenant Tester(int days = 90) => new() { TesterSince = Since, TesterUntil = Since.AddDays(days) };

    [Fact]
    public void Weeks_count_from_start()
    {
        TesterRules.Week(Since, Since.AddDays(6.9)).Should().Be(0);
        TesterRules.Week(Since, Since.AddDays(7)).Should().Be(1);
        TesterRules.Week(Since, Since.AddDays(20)).Should().Be(2);
        TesterRules.Week(Since, Since.AddDays(-1)).Should().Be(0);
        TesterRules.TotalWeeks(Since, Since.AddDays(90)).Should().Be(13);
    }

    [Fact]
    public void Survey_waits_from_end_of_first_week_until_answered()
    {
        var t = Tester();
        TesterRules.FeedbackDue(t, Since.AddDays(3), []).Should().BeFalse("w pierwszym tygodniu jeszcze nie pytamy");
        TesterRules.FeedbackDue(t, Since.AddDays(8), []).Should().BeTrue();
        TesterRules.FeedbackDue(t, Since.AddDays(8), [1]).Should().BeFalse();
        TesterRules.FeedbackDue(t, Since.AddDays(15), [1]).Should().BeTrue("nowy tydzień — nowa ankieta");
        TesterRules.FeedbackDue(t, Since.AddDays(91), []).Should().BeFalse("po programie nie pytamy");
        TesterRules.FeedbackDue(new Tenant(), Since, []).Should().BeFalse();
    }

    [Fact]
    public void Missed_weeks_stop_at_program_end()
    {
        var t = Tester(28);
        TesterRules.Missed(t, Since.AddDays(15), [1]).Should().Be(1);
        TesterRules.Missed(t, Since.AddDays(15), [1, 2]).Should().Be(0);
        TesterRules.Missed(t, Since.AddDays(200), [1]).Should().Be(3, "liczymy tylko tygodnie programu (4 tygodnie, opinia za pierwszy)");
    }

    [Fact]
    public void Later_keeps_the_later_date()
    {
        TesterRules.Later(null, Since).Should().Be(Since);
        TesterRules.Later(Since.AddDays(5), Since).Should().Be(Since.AddDays(5));
        TesterRules.Later(Since.AddDays(-5), Since).Should().Be(Since);
    }
}
