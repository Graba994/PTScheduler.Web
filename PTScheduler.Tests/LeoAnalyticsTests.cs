using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Leo — analiza: ostrzeżenia, segmenty, pieniądze zagrożone, głos klientów i gotowe pytania (same reguły, bez AI).</summary>
public class LeoAnalyticsTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    private static LeoAnalytics.Studio Studio(string name = "Studio A") => new(
        Id: 1, Name: name, Slug: "a", OwnerEmail: "a@example.com", PlanId: "start", PlanName: "Start", ClientLimit: 30, MonthlyValue: 99m, Paying: true,
        Clients: 12, Clients7Ago: 12, Clients30Ago: 11, ActiveClients30: 10,
        Sessions7: 20, Sessions7Prev: 19, Cancelled7: 1, NoShow7: 0, Upcoming7: 18,
        Revenue30: 2000m, Revenue30Prev: 1900m, Workouts30: 40, Messages30: 30, Plans: 5,
        LastActivityAt: Now.AddDays(-1), ProvisionedAt: Now.AddDays(-120), RiskScore: 0, RiskReasons: []);

    private static List<Plan> Plans() =>
    [
        new() { Id = "start", Name = "Start", MonthlyPrice = 99, MaxClients = 30, IsActive = true, SortOrder = 1 },
        new() { Id = "pro", Name = "Pro", MonthlyPrice = 199, MaxClients = 100, IsActive = true, SortOrder = 2 },
        new() { Id = "studio", Name = "Studio", MonthlyPrice = 399, MaxClients = 9999, IsActive = true, SortOrder = 3 },
    ];

    [Fact]
    public void Steady_studio_has_no_warnings_and_no_segment()
    {
        LeoAnalytics.Warnings([Studio()]).Should().BeEmpty();
        LeoAnalytics.Segments([Studio()], Plans(), Now).Should().OnlyContain(s => s.Items.Count == 0);
    }

    [Fact]
    public void Halved_sessions_warn_with_percentage()
    {
        var w = LeoAnalytics.Warnings([Studio() with { Sessions7 = 4, Sessions7Prev = 20 }]).Single();
        w.Kind.Should().Be("spadek_wizyt");
        w.Title.Should().Be("Mniej wizyt o 80%");
        w.Severity.Should().Be(3);
    }

    [Fact]
    public void Small_numbers_do_not_warn_about_drop()
    {
        LeoAnalytics.Warnings([Studio() with { Sessions7 = 1, Sessions7Prev = 4 }]).Should().NotContain(w => w.Kind == "spadek_wizyt");
    }

    [Fact]
    public void No_shows_and_cancellations_warn_by_share()
    {
        var list = LeoAnalytics.Warnings([Studio() with { Sessions7 = 6, NoShow7 = 3, Cancelled7 = 5 }]);
        list.Select(w => w.Kind).Should().Contain(["nieobecnosci", "odwolania"]);
    }

    [Fact]
    public void Empty_calendar_after_active_month_warns()
    {
        LeoAnalytics.Warnings([Studio() with { Upcoming7 = 0 }]).Should().ContainSingle(w => w.Kind == "pusty_kalendarz");
    }

    [Fact]
    public void Older_app_without_activity_data_gives_no_activity_warnings()
    {
        var old = Studio() with { Sessions7 = null, Sessions7Prev = null, Cancelled7 = null, NoShow7 = null, Upcoming7 = null, ActiveClients30 = null, Revenue30 = null, Revenue30Prev = null };
        LeoAnalytics.Warnings([old]).Should().BeEmpty();
    }

    [Fact]
    public void Segments_find_growing_near_limit_and_dormant()
    {
        var growing = Studio("Rośnie") with { Clients = 18, Clients30Ago = 12 };
        var full = Studio("Pełne") with { Clients = 26 };
        var asleep = Studio("Śpi") with { Sessions7 = 0, Upcoming7 = 0, LastActivityAt = Now.AddDays(-20) };
        var segs = LeoAnalytics.Segments([growing, full, asleep], Plans(), Now).ToDictionary(s => s.Key);

        segs[LeoAnalytics.Growing].Items.Should().ContainSingle(i => i.Studio.Name == "Rośnie").Which.Note.Should().Contain("+6");
        segs[LeoAnalytics.NearLimit].Items.Select(i => i.Studio.Name).Should().Contain("Pełne");
        segs[LeoAnalytics.NearLimit].Items.Single(i => i.Studio.Name == "Pełne").Note.Should().Contain("26 z 30").And.Contain("Pro");
        segs[LeoAnalytics.Dormant].Items.Should().ContainSingle(i => i.Studio.Name == "Śpi").Which.Note.Should().Contain("20 dni");
    }

    [Fact]
    public void New_studio_is_not_dormant_in_first_week()
    {
        var fresh = Studio() with { Sessions7 = 0, Upcoming7 = 0, ProvisionedAt = Now.AddDays(-3) };
        LeoAnalytics.Segments([fresh], Plans(), Now).Single(s => s.Key == LeoAnalytics.Dormant).Items.Should().BeEmpty();
    }

    [Fact]
    public void Unlimited_plan_counts_as_no_limit()
    {
        LeoAnalytics.ClientLimit(Plans()[2]).Should().Be(0);
        LeoAnalytics.NextPlan(Plans(), Studio() with { PlanId = "pro", ClientLimit = 100 })!.Id.Should().Be("studio");
    }

    [Fact]
    public void Money_at_risk_weights_value_by_score()
    {
        var money = LeoAnalytics.MoneyAtRisk(
        [
            Studio("Ryzyko") with { RiskScore = 60, MonthlyValue = 200m, RiskReasons = ["Brak aktywności od 25 dni"] },
            Studio("Średnie") with { Id = 2, RiskScore = 30, MonthlyValue = 100m },
            Studio("Spokój") with { Id = 3, RiskScore = 10, MonthlyValue = 500m },
        ]);
        money.Items.Select(i => i.Studio.Name).Should().Equal("Ryzyko", "Średnie");
        money.Items[0].Expected.Should().Be(120m);
        money.MonthlyAtRisk.Should().Be(300m);
        money.ExpectedMonthly.Should().Be(150m);
        money.YearlyAtRisk.Should().Be(3600m);
    }

    [Fact]
    public void Voice_ranks_topics_and_scrubs_contacts()
    {
        var voice = LeoAnalytics.RankVoice(
        [
            ("ankieta", "Brakuje płatności BLIK za karnety"),
            ("ocena", "Chciałbym płacić przelewem, kontakt jan@example.com"),
            ("spotkanie", "Dieta i jadłospisy dla klientów"),
            ("zgloszenie", "Coś zupełnie innego"),
        ]);
        voice.Texts.Should().Be(4);
        voice.Topics[0].Key.Should().Be("platnosci");
        voice.Topics[0].Mentions.Should().Be(2);
        voice.Topics[0].Sources.Keys.Should().BeEquivalentTo(["ankieta", "ocena"]);
        voice.Topics.SelectMany(t => t.Quotes).Should().NotContain(q => q.Contains("jan@example.com"));
        voice.Unmatched.Should().Be(1);
    }

    [Fact]
    public void Presets_answer_without_ai()
    {
        var studios = new List<LeoAnalytics.Studio> { Studio() with { RiskScore = 60, RiskReasons = ["Zgłoszono rezygnację 10.10"] } };
        var o = new LeoAnalytics.Overview(Now, studios, LeoAnalytics.Warnings(studios), LeoAnalytics.Segments(studios, Plans(), Now),
            LeoAnalytics.MoneyAtRisk(studios), LeoAnalytics.RankVoice([]));

        foreach (var (key, _) in LeoAnalytics.Presets)
        {
            var a = LeoAnalytics.AnswerPreset(key, o);
            a.FromAi.Should().BeFalse();
            a.Text.Should().NotBeNullOrWhiteSpace().And.NotContain("Nie znam");
        }
        LeoAnalytics.AnswerPreset("odejda", o).Text.Should().Contain("Studio A").And.Contain("60 pkt");
        LeoAnalytics.AnswerPreset("pieniadze", o).Text.Should().Contain("99 zł");
    }

    [Theory]
    [InlineData("Ile pieniędzy możemy stracić?", "pieniadze")]
    [InlineData("Kto może zrezygnować?", "odejda")]
    [InlineData("Komu się kończy limit?", "limit")]
    [InlineData("Jakie funkcje chcą trenerzy?", "glos")]
    [InlineData("Jak było w tym tygodniu?", "tydzien")]
    [InlineData("Ulubiony kolor?", null)]
    public void Free_question_maps_to_preset_without_ai(string question, string? expected) =>
        LeoAnalytics.MatchPreset(question).Should().Be(expected);

    [Fact]
    public void Data_pack_for_ai_has_no_owner_contacts()
    {
        var studios = new List<LeoAnalytics.Studio> { Studio() };
        var o = new LeoAnalytics.Overview(Now, studios, [], LeoAnalytics.Segments(studios, Plans(), Now), LeoAnalytics.MoneyAtRisk(studios), LeoAnalytics.RankVoice([]));
        var json = System.Text.Json.JsonSerializer.Serialize(LeoAnalytics.Pack(o));
        json.Should().Contain("Studio A").And.NotContain("a@example.com");
    }

    [Fact]
    public void Back_picks_snapshot_about_a_week_old()
    {
        var d = DateOnly.FromDateTime(Now);
        var list = new List<TenantMetricSnapshot>
        {
            new() { Date = d.AddDays(-15), SessionsTotal = 1 },
            new() { Date = d.AddDays(-8), SessionsTotal = 2 },
            new() { Date = d, SessionsTotal = 3 },
        };
        LeoAnalytics.Back(list, 7)!.SessionsTotal.Should().Be(2);
        LeoAnalytics.Back(list, 30).Should().BeNull();
    }

    [Fact]
    public void Weekly_delta_reads_naturally()
    {
        new LeoWeeklyReport.Pair(12, 10).Delta.Should().Be("+20%");
        new LeoWeeklyReport.Pair(5, 10).Delta.Should().Be("−50%");
        new LeoWeeklyReport.Pair(0, 0).Delta.Should().Be("bez zmian");
        new LeoWeeklyReport.Pair(3, 0).Delta.Should().Be("nowe");
    }
}
