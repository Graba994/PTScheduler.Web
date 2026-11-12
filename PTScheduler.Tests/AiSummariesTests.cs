using System.Text.RegularExpressions;
using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Progress;
using PTScheduler.Application.Reports;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Podsumowania z AI: „Co się zmieniło” (raport biznesowy), „W skrócie” (profil klienta), „Twój miesiąc/rok” (klient).
/// Reguły liczą fakty i tekst podstawowy; AI może go tylko przeredagować — każda liczba z tekstu musi być w faktach.
/// </summary>
public class AiSummariesTests
{
    private static readonly Regex Numbers = new(@"\d+(?:[.,]\d+)?");

    /// <summary>Ten sam warunek co strażnik w Portalu: tekst podstawowy nie może mieć liczb spoza faktów.</summary>
    private static void NumbersComeFromFacts(string text, Dictionary<string, string> facts)
    {
        var source = string.Join(" ", facts.Select(f => f.Key + " " + f.Value));
        var allowed = Numbers.Matches(source).Select(m => m.Value.Replace(',', '.')).ToHashSet();
        Numbers.Matches(text).Select(m => m.Value.Replace(',', '.')).Should().OnlyContain(n => allowed.Contains(n), $"tekst: {text}");
    }

    // ── Co się zmieniło ──

    private static BusinessReportDto Report(int? ret = 80, int? retPrev = 100, decimal revenue = 11420, decimal revenuePrev = 7850,
        int paying = 4, decimal avg = 2855, decimal avgPrev = 1570, int lost = 1, int newClients = 0, int? occ = 13) => new()
    {
        PeriodDays = 90,
        RetentionPercent = ret, RetentionPercentPrevious = retPrev,
        LostClients = Enumerable.Range(1, lost).Select(i => new ClientRefDto(i, $"Klient {i}", null)).ToList(),
        NewClients = newClients,
        Revenue = revenue, RevenuePrevious = revenuePrev, PayingClients = paying,
        AvgRevenuePerClient = avg, AvgRevenuePerClientPrevious = avgPrev,
        PastOccupancy = new OccupancyDto { AvailableMinutes = occ is null ? 0 : 12540, BookedMinutes = 1608, Percent = occ },
        OccupancyByWeekday =
        [
            new("Pon", 600, 300, 50), new("Czw", 600, 30, 5), new("Pt", 600, 60, 10), new("Sob", 0, 0, null),
        ],
    };

    [Fact]
    public void Changes_explain_retention_drop_and_revenue_reason()
    {
        var r = BusinessChanges.Compute(Report());

        r.Items.Select(i => i.Kind).Should().StartWith(["retencja", "przychod"]);
        r.Items[0].Tone.Should().Be("bad");
        r.Items[0].Text.Should().Contain("spadła o 20 pp").And.Contain("1 osoba przestała przychodzić");
        r.Items[1].Text.Should().Contain("wzrósł o 45%").And.Contain("większym zakupom");
        r.Items.Should().Contain(i => i.Kind == "oblozenie" && i.Text.Contains("w czwartki i w piątki"));
        r.Fallback.Should().StartWith(r.Items[0].Text).And.EndWith("do uwagi — szczegóły poniżej.");
        NumbersComeFromFacts(r.Fallback, r.Facts);
    }

    [Fact]
    public void Changes_never_send_client_names_to_ai()
    {
        var r = BusinessChanges.Compute(Report(lost: 3));
        string.Join(" ", r.Facts.Values).Should().NotContain("Klient");
        r.Fallback.Should().NotContain("Klient");
    }

    [Fact]
    public void Small_changes_give_calm_summary()
    {
        var r = BusinessChanges.Compute(Report(ret: 82, retPrev: 80, revenue: 1000, revenuePrev: 980, lost: 0, occ: 60));
        r.Items.Should().BeEmpty();
        r.Fallback.Should().Contain("Bez większych zmian");
    }

    [Fact]
    public void More_paying_clients_is_named_as_revenue_reason()
    {
        var r = BusinessChanges.Compute(Report(revenue: 3000, revenuePrev: 2000, paying: 6, avg: 500, avgPrev: 500, lost: 0));
        r.Items.Should().Contain(i => i.Kind == "przychod" && i.Text.Contains("płaci więcej osób"));
    }

    // ── W skrócie ──

    private static ClientProgressDto Progress(int attended = 6, int missed = 0) => new()
    {
        CompletedSessions = 20, Attended30 = attended, Missed30 = missed,
        Strength = [new("Wyciskanie sztangi leżąc", 60, 70)],
    };

    private static WellbeingSummary Wellbeing(TrendDirection dir, int pain = 0) =>
        new(Enumerable.Range(0, 6).Select(i => new WellbeingPoint(new DateOnly(2026, 10, 1 + i), 3, false)).ToList(), dir, 2.5m, 3.6m, pain, 3, "");

    [Fact]
    public void Snapshot_combines_progress_wellbeing_and_package_with_one_advice()
    {
        var r = ClientSnapshot.Compute(new(Progress(), Wellbeing(TrendDirection.Down), null, 0, 2, 3));

        r.Points.Select(p => p.Kind).Should().Contain(["frekwencja", "sila", "samopoczucie", "pakiet"]);
        r.Points.First().Tone.Should().Be("bad", "najpierw to, co niepokoi");
        r.Advice.Should().Contain("regenerację");
        r.Headline.Should().Be("Trenuje regularnie i ciężary rosną, ale samopoczucie spada i pakiet zaraz się skończy.");
        r.Fallback.Should().Be(r.Headline + " " + r.Advice);
        NumbersComeFromFacts(r.Fallback, r.Facts);
    }

    [Fact]
    public void Snapshot_puts_churn_risk_first_and_uses_its_suggestion()
    {
        var risk = new ClientChurnRules.Result(55, [new(55, "14 dni bez treningu", DraftPurpose.Comeback, "Napisz krótko i zaproponuj termin.")]);
        var r = ClientSnapshot.Compute(new(Progress(), null, risk, 0, 5, 20));

        r.Points[0].Kind.Should().Be("ryzyko");
        r.Points.Should().Contain(p => p.Kind == "przerwa" && p.Text.Contains("20 dni"));
        r.Advice.Should().Be("Napisz krótko i zaproponuj termin.");
        NumbersComeFromFacts(r.Fallback, r.Facts);
    }

    [Fact]
    public void Snapshot_without_data_is_hidden()
    {
        ClientSnapshot.Compute(new(null, null, null, 0, null, null)).HasData.Should().BeFalse();
    }

    [Fact]
    public void Snapshot_text_is_gender_neutral()
    {
        var r = ClientSnapshot.Compute(new(Progress(attended: 2, missed: 3), Wellbeing(TrendDirection.Up, pain: 1), null, 1, 0, 3));
        r.Fallback.Should().NotMatchRegex(@"\b\w+(ł|ła)\b(?<!przestała|została)");
        r.Fallback.Should().NotContain("/a").And.NotContain("(a)");
    }

    // ── Twój miesiąc / rok ──

    private static RecapInput Month(int days = 9, int prev = 7, int records = 2, params string[] medals) =>
        new(false, new DateOnly(2026, 10, 1), days, prev, 6, 4, 1, records, ("Przysiad ze sztangą", 80), null, medals);

    [Fact]
    public void Month_recap_compares_with_previous_month_in_polish()
    {
        var r = ClientRecap.Compute(Month(medals: "10 treningów"));

        r.Title.Should().Be("Twój październik");
        r.Fallback.Should().StartWith("W październiku 9 treningów — o 2 więcej niż we wrześniu.");
        r.Fallback.Should().Contain("6 tygodni z rzędu").And.Contain("Przysiad ze sztangą — 80 kg").And.Contain("Nowy medal: 10 treningów");
        r.Tiles.Select(t => t.Label).Should().Equal("treningów", "tyg. z rzędu", "rekordy", "medal");
        r.PushBody.Should().Be("W październiku: 9 treningów i 2 rekordy. Zobacz podsumowanie.");
        NumbersComeFromFacts(r.Fallback, r.Facts);
    }

    [Fact]
    public void Fewer_trainings_get_gentle_encouragement()
    {
        var r = ClientRecap.Compute(Month(days: 3, prev: 8, records: 0));
        r.Fallback.Should().Contain("łatwo wrócić do rytmu").And.NotContain("mniej");
        NumbersComeFromFacts(r.Fallback, r.Facts);
    }

    [Fact]
    public void No_trainings_means_no_recap()
    {
        ClientRecap.Compute(Month(days: 0)).HasData.Should().BeFalse();
    }

    [Fact]
    public void Year_recap_shows_best_streak_and_biggest_gain()
    {
        var r = ClientRecap.Compute(new(true, new DateOnly(2025, 1, 1), 96, 40, 2, 14, 11, 12, null, ("Wyciskanie sztangi leżąc", 50, 65), ["Rok razem"]));

        r.Title.Should().Be("Twój rok 2025");
        r.Fallback.Should().StartWith("W 2025 roku 96 treningów w 11 miesiącach — o 56 więcej niż w 2024.");
        r.Fallback.Should().Contain("Najdłuższa seria: 14 tygodni").And.Contain("z 50 do 65 kg");
        NumbersComeFromFacts(r.Fallback, r.Facts);
    }

    [Theory]
    [InlineData("2026-11-01", false, "2026-10-01", "2026-11-01")]
    [InlineData("2026-03-05", false, "2026-02-01", "2026-03-01")]
    [InlineData("2027-01-02", true, "2026-01-01", "2027-01-01")]
    public void Period_is_previous_month_or_previous_year_in_january(string today, bool year, string from, string to)
    {
        ClientRecap.PeriodFor(DateOnly.Parse(today)).Should().Be((year, DateOnly.Parse(from), DateOnly.Parse(to)));
    }

    [Fact]
    public void Records_count_only_inside_period_against_whole_history()
    {
        var d = (int day, int month) => new DateOnly(2026, month, day);
        var sessions = new List<(int, DateOnly, decimal)>
        {
            (1, d(5, 9), 60), (1, d(20, 9), 65),   // wrzesień: rekord 65 — poza okresem
            (1, d(3, 10), 65), (1, d(17, 10), 70), // październik: 70 > 65 → rekord
            (2, d(10, 10), 40),                    // pierwszy raz — to nie rekord
            (1, d(2, 11), 75),                     // listopad — poza okresem
        };
        var (count, top) = ClientRecap.RecordsIn(sessions, d(1, 10), d(1, 11));
        count.Should().Be(1);
        top.Should().Be((1, 70m));
    }

    [Fact]
    public void Top_gain_uses_first_and_best_result_in_period()
    {
        var d = (int day) => new DateOnly(2025, 3, day);
        var gain = ClientRecap.TopGain([(1, d(1), 50), (1, d(10), 60), (1, d(20), 65), (2, d(2), 100), (2, d(9), 105)], d(1), d(31));
        gain.Should().Be((1, 50m, 65m));
    }
}
