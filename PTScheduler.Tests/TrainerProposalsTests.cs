using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Messaging;
using PTScheduler.Application.Scheduling;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Wolne okienka, zaproszenia (szablon, SMS, krótki link) i ciche propozycje asystenta — same reguły.</summary>
public class TrainerProposalsTests
{
    // Piątek 16.10.2026, 8:00 — jutro sobota.
    private static readonly DateTime Now = new(2026, 10, 16, 8, 0, 0);
    private static readonly DateTime UtcNow = new(2026, 10, 16, 6, 0, 0, DateTimeKind.Utc);

    private static AvailableSlotDto Slot(int h, int m, bool free = true, int minutes = 60) =>
        new() { Start = Now.Date.AddDays(1).AddHours(h).AddMinutes(m), End = Now.Date.AddDays(1).AddHours(h).AddMinutes(m + minutes), IsAvailable = free };

    [Fact]
    public void Merge_joins_overlapping_free_slots_into_windows()
    {
        // Siatka co 30 min, wizyta 60 min: 10:00–12:00 wolne, 12:00 zajęte, 15:00–16:00 wolne.
        var slots = new[] { Slot(10, 0), Slot(10, 30), Slot(11, 0), Slot(12, 0, false), Slot(15, 0) };
        var windows = FreeSlots.Merge(slots, Now);
        windows.Should().HaveCount(2);
        windows[0].Start.Hour.Should().Be(10);
        windows[0].End.Hour.Should().Be(12);
        windows[0].Minutes.Should().Be(120);
        windows[1].Minutes.Should().Be(60);
    }

    [Fact]
    public void Merge_skips_short_and_past_windows()
    {
        FreeSlots.Merge([Slot(10, 0, minutes: 30)], Now, 60).Should().BeEmpty();
        FreeSlots.Merge([Slot(10, 0)], Now.AddDays(2)).Should().BeEmpty();
    }

    private static FreeSlots.ClientHistory History(int id, params DateTime[] completed) =>
        new(id, completed.Select(d => (d, 3)).ToList(), null, null);

    [Fact]
    public void Rank_prefers_clients_who_train_at_that_time_on_that_weekday()
    {
        var window = new FreeSlots.Window(Now.Date.AddDays(1).AddHours(10), Now.Date.AddDays(1).AddHours(12));
        var saturdays = History(1, Now.AddDays(-6).Date.AddHours(10), Now.AddDays(-13).Date.AddHours(10), Now.AddDays(-20).Date.AddHours(10));
        var weekdays = History(2, Now.AddDays(-2).Date.AddHours(11), Now.AddDays(-9).Date.AddHours(11), Now.AddDays(-16).Date.AddHours(11));
        var evenings = History(3, Now.AddDays(-2).Date.AddHours(19), Now.AddDays(-9).Date.AddHours(19));

        var ranked = FreeSlots.Rank(window, [weekdays, evenings, saturdays], Now, UtcNow, 60);
        ranked.Select(r => r.ClientId).Should().Equal(1, 2);
        ranked[0].SameWeekday.Should().Be(3);
        ranked[0].SuggestedStart.Hour.Should().Be(10);
        ranked[1].SuggestedStart.Hour.Should().Be(11);
    }

    [Fact]
    public void Rank_skips_booked_and_recently_invited_clients()
    {
        var window = new FreeSlots.Window(Now.Date.AddDays(1).AddHours(10), Now.Date.AddDays(1).AddHours(12));
        var dates = new[] { Now.AddDays(-6).Date.AddHours(10), Now.AddDays(-13).Date.AddHours(10) };
        var booked = History(1, dates) with { NextScheduled = Now.AddDays(3) };
        var invited = History(2, dates) with { LastInvitedUtc = UtcNow.AddDays(-2) };
        var invitedLongAgo = History(3, dates) with { LastInvitedUtc = UtcNow.AddDays(-10) };
        FreeSlots.Rank(window, [booked, invited, invitedLongAgo], Now, UtcNow, 60).Select(r => r.ClientId).Should().Equal(3);
    }

    [Fact]
    public void Describe_reads_naturally()
    {
        FreeSlots.Describe(new(Now.Date.AddDays(1).AddHours(10), Now.Date.AddDays(1).AddHours(12)), Now).Should().Be("jutro (sobota) 10:00–12:00");
        FreeSlots.Describe(new(Now.Date.AddDays(4).AddHours(17), Now.Date.AddDays(4).AddHours(19)), Now).Should().Be("we wtorek 20.10, 17:00–19:00");
    }

    [Fact]
    public void Slot_invite_template_keeps_name_and_link_tags()
    {
        var style = new MessageTemplates.Style("ty", "tak", null, "Jan");
        var text = MessageTemplates.Build(DraftPurpose.SlotInvite, style, new Dictionary<string, string> { ["termin"] = "jutro (sobota) 10:00–12:00" });
        text.Should().Contain(MessageTemplates.ClientTag).And.Contain(MessageTemplates.LinkTag).And.Contain("jutro (sobota) 10:00–12:00").And.EndWith("Jan");

        var noSelf = MessageTemplates.Build(DraftPurpose.SlotInvite, style, new Dictionary<string, string> { ["termin"] = "jutro", ["rezerwacja"] = "przez_trenera" });
        noSelf.Should().NotContain(MessageTemplates.LinkTag);
    }

    [Fact]
    public void Sms_version_drops_diacritics_and_emoji_but_keeps_the_link()
    {
        var sms = MessageTemplates.ForSms("Cześć Ania! Zwolniło mi się okienko: jutro 10:00–12:00. Wpadniesz? Zarezerwujesz tutaj: https://ab.pl/l/k7Pq2xM 🙂\nJan");
        sms.Should().Be("Czesc Ania! Zwolnilo mi sie okienko: jutro 10:00-12:00. Wpadniesz? Zarezerwujesz tutaj: https://ab.pl/l/k7Pq2xM Jan");

        var longText = new string('a', 400) + " https://ab.pl/l/abc1234";
        var cut = MessageTemplates.ForSms(longText, 160);
        cut.Length.Should().BeLessThanOrEqualTo(160);
        cut.Should().EndWith("https://ab.pl/l/abc1234");
    }

    [Theory]
    [InlineData("/my?termin=2026-10-17T10:00&typ=3", true)]
    [InlineData("/chat", true)]
    [InlineData("//evil.com", false)]
    [InlineData("https://evil.com", false)]
    [InlineData("/\\evil.com", false)]
    [InlineData("", false)]
    public void Short_link_only_leads_inside_the_app(string path, bool ok) =>
        ShortLinks.IsSafeTarget(path).Should().Be(ok);

    [Fact]
    public void Short_codes_are_short_and_unambiguous()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => ShortLinks.NewCode()).ToList();
        codes.Should().OnlyContain(c => c.Length == 7 && !c.Any(ch => "0O1lI".Contains(ch)));
        codes.Distinct().Count().Should().Be(200);
    }

    // ── Ciche propozycje ──

    [Fact]
    public void Quiet_client_is_measured_against_personal_rhythm()
    {
        var weekly = Enumerable.Range(0, 6).Select(i => Now.AddDays(-30 - 7 * i)).ToList();
        var gap = TrainerNudges.MedianGapDays(weekly);
        gap.Should().Be(7);
        TrainerNudges.IsUnusuallyQuiet(gap, 30, hasUpcoming: false).Should().BeTrue();
        TrainerNudges.IsUnusuallyQuiet(gap, 30, hasUpcoming: true).Should().BeFalse();
        TrainerNudges.IsUnusuallyQuiet(gap, 9, hasUpcoming: false).Should().BeFalse();
        TrainerNudges.IsUnusuallyQuiet(gap, 90, hasUpcoming: false).Should().BeFalse();
        TrainerNudges.MedianGapDays(weekly.Take(3)).Should().BeNull();
    }

    [Fact]
    public void No_shows_count_only_last_four_held_visits()
    {
        TrainerNudges.RecentNoShows([SessionStatus.NoShow, SessionStatus.Cancelled, SessionStatus.Completed, SessionStatus.NoShow, SessionStatus.Completed, SessionStatus.NoShow])
            .Should().Be(2);
    }

    [Fact]
    public void Milestones_are_reported_once_when_crossed()
    {
        TrainerNudges.CrossedMilestone(495, 503).Should().Be(500);
        TrainerNudges.CrossedMilestone(500, 510).Should().BeNull();
        TrainerNudges.CrossedMilestone(90, 99).Should().BeNull();
    }

    [Fact]
    public void Overload_needs_volume_above_usual_and_busy_evenings()
    {
        TrainerNudges.IsOverloaded(34, [24, 26, 25], 5).Should().BeTrue();
        TrainerNudges.IsOverloaded(34, [33, 34, 32], 6).Should().BeFalse();
        TrainerNudges.IsOverloaded(34, [24, 26], 3).Should().BeFalse();
    }

    [Fact]
    public void Price_hint_needs_six_full_weeks()
    {
        var rules = new List<TrainerAvailabilityDto>
        {
            new() { DayOfWeek = DayOfWeek.Monday, StartTime = new(8, 0), EndTime = new(12, 0), IsActive = true },
            new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(16, 0), EndTime = new(20, 0), IsActive = true },
            new() { DayOfWeek = DayOfWeek.Friday, StartTime = new(8, 0), EndTime = new(10, 0), IsActive = false },
        };
        var week = TrainerNudges.WeekStart(new DateOnly(2026, 10, 16));
        week.Should().Be(new DateOnly(2026, 10, 12));
        TrainerNudges.AvailableMinutes(rules, week).Should().Be(480);
        TrainerNudges.Occupancy(450, 480).Should().BeApproximately(0.94, 0.01);
        TrainerNudges.SuggestPriceReview([0.95, 0.92, 0.9, 1.0, 0.97, 0.93]).Should().BeTrue();
        TrainerNudges.SuggestPriceReview([0.95, 0.92, 0.8, 1.0, 0.97, 0.93]).Should().BeFalse();
        TrainerNudges.SuggestPriceReview([0.95, 0.92]).Should().BeFalse();
    }

    [Fact]
    public void Stalled_plan_means_no_weight_increase_across_six_workouts()
    {
        IReadOnlyDictionary<int, decimal> W(decimal squat, decimal bench) => new Dictionary<int, decimal> { [1] = squat, [2] = bench };
        TrainerNudges.IsStalled([W(60, 40), W(60, 40), W(60, 40), W(60, 40), W(57.5m, 40), W(60, 40)]).Should().BeTrue();
        TrainerNudges.IsStalled([W(60, 40), W(60, 40), W(60, 40), W(62.5m, 40), W(60, 40), W(60, 40)]).Should().BeFalse();
        TrainerNudges.IsStalled([W(60, 40), W(60, 40)]).Should().BeNull();
    }
}
