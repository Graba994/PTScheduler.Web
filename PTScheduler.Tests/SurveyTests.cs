using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Ankiety: walidacja i flagi, domyślne szablony, zapis z powiadomieniem trenera,
/// statystyki i wnioski (ból, ACWR, trend RPE).
/// </summary>
public class SurveyTests
{
    private static readonly DateOnly Today = new(2026, 6, 30);

    private static Dictionary<string, SurveyAnswerInput> ValidHealth(bool heart = false) => new()
    {
        ["parq_heart"] = new() { Bool = heart },
        ["parq_chest_pain"] = new() { Bool = false },
        ["parq_dizziness"] = new() { Bool = false },
        ["parq_chronic"] = new() { Bool = false },
        ["parq_meds"] = new() { Bool = false },
        ["parq_joint"] = new() { Bool = false },
        ["parq_supervised"] = new() { Bool = false },
        ["activity_level"] = new() { Choices = ["1–2 razy w tygodniu"] },
        ["goals"] = new() { Choices = ["Siła", "Nieistniejąca opcja"] },
        ["consent"] = new() { Bool = true }
    };

    [Fact]
    public void Defaults_Have_Unique_Keys_And_Are_Valid()
    {
        foreach (var kind in Enum.GetValues<SurveyKind>())
            SurveyEvaluator.ValidateTemplate(SurveyDefaults.For(kind)).Should().BeEmpty();
    }

    [Fact]
    public void Health_ParQ_Yes_Is_Flagged_And_Consent_Required()
    {
        var q = SurveyDefaults.HealthIntake().Questions;

        var (errors, answers) = SurveyEvaluator.Evaluate(q, ValidHealth(heart: true));
        errors.Should().BeEmpty();
        answers.Single(a => a.Key == "parq_heart").Flagged.Should().BeTrue();
        answers.Single(a => a.Key == "goals").Choices.Should().Equal("Siła"); // nieznane opcje odrzucone

        var noConsent = ValidHealth();
        noConsent["consent"] = new() { Bool = false };
        SurveyEvaluator.Evaluate(q, noConsent).Errors.Should().ContainSingle(e => e.Contains("potwierdzenie"));
    }

    [Fact]
    public void Scale_Out_Of_Range_Is_Rejected()
    {
        var q = SurveyDefaults.PostWorkout().Questions;
        var (errors, _) = SurveyEvaluator.Evaluate(q, new Dictionary<string, SurveyAnswerInput>
        {
            [SurveyKeys.Rpe] = new() { Number = 11 },
            [SurveyKeys.Pain] = new() { Bool = false }
        });
        errors.Should().Contain(e => e.Contains("od 0 do 10"));
    }

    [Fact]
    public async Task Submit_Health_Notifies_Trainer_And_Marks_Pending_Done()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "cu", FirstName = "Anna", LastName = "Nowak", TrainerUserId = "t1" });
            await db.SaveChangesAsync();
        }
        var push = new Mock<IWebPushService>();
        var svc = new SurveyService(f, push.Object, TestClock.AtWallClock(new DateTime(2026, 6, 30, 12, 0, 0)), NullLogger<SurveyService>.Instance);

        (await svc.IsHealthSurveyPendingAsync(1)).Should().BeTrue();
        var (errors, response) = await svc.SubmitAsync(1, SurveyKind.HealthIntake, ValidHealth(heart: true));

        errors.Should().BeEmpty();
        response!.FlagCount.Should().Be(1);
        (await svc.IsHealthSurveyPendingAsync(1)).Should().BeFalse();
        push.Verify(p => p.SendAsync("t1", It.Is<PushMessageDto>(m => m.Url == "/clients/1?tab=surveys")), Times.Once);
    }

    [Fact]
    public async Task PostWorkout_Same_Day_Overwrites_And_Unflagged_Does_Not_Push()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "cu", FirstName = "Anna", TrainerUserId = "t1" });
            await db.SaveChangesAsync();
        }
        var push = new Mock<IWebPushService>();
        var svc = new SurveyService(f, push.Object, TestClock.AtWallClock(new DateTime(2026, 6, 30, 12, 0, 0)), NullLogger<SurveyService>.Instance);
        Dictionary<string, SurveyAnswerInput> Answers(int rpe) => new()
        {
            [SurveyKeys.Rpe] = new() { Number = rpe },
            [SurveyKeys.Pain] = new() { Bool = false }
        };

        await svc.SubmitAsync(1, SurveyKind.PostWorkout, Answers(6), Today);
        await svc.SubmitAsync(1, SurveyKind.PostWorkout, Answers(8), Today);

        var all = await svc.GetResponsesAsync(1, SurveyKind.PostWorkout);
        all.Should().ContainSingle();
        all[0][SurveyKeys.Rpe]!.Number.Should().Be(8);
        push.Verify(p => p.SendAsync(It.IsAny<string>(), It.IsAny<PushMessageDto>()), Times.Never);
    }

    private static SurveyResponseDto Resp(DateOnly date, decimal rpe, decimal minutes, bool pain = false, string? where = null) => new()
    {
        Kind = SurveyKind.PostWorkout,
        WorkoutDate = date,
        SubmittedAtUtc = date.ToDateTime(new TimeOnly(18, 0)),
        Answers =
        [
            new() { Key = SurveyKeys.Rpe, Type = SurveyQuestionType.Scale, Number = rpe, Max = 10 },
            new() { Key = SurveyKeys.Duration, Type = SurveyQuestionType.Number, Number = minutes },
            new() { Key = SurveyKeys.Pain, Type = SurveyQuestionType.YesNo, Bool = pain },
            new() { Key = SurveyKeys.PainLocation, Type = SurveyQuestionType.Text, Text = where }
        ]
    };

    [Fact]
    public void Insights_Report_Pain_And_Acwr_Spike()
    {
        // 3 spokojne tygodnie (RPE 5 × 60 min, 2×/tydz.), potem ciężki tydzień (RPE 9 × 90 min, 3×).
        var list = new List<SurveyResponseDto>();
        for (var w = 4; w >= 2; w--)
        {
            list.Add(Resp(Today.AddDays(-7 * w + 1), 5, 60));
            list.Add(Resp(Today.AddDays(-7 * w + 4), 5, 60));
        }
        list.Add(Resp(Today.AddDays(-5), 9, 90));
        list.Add(Resp(Today.AddDays(-3), 9, 90, pain: true, where: "kolano przy przysiadzie"));
        list.Add(Resp(Today.AddDays(-1), 9, 90));

        var stats = SurveyInsights.Compute(list, SurveyDefaults.PostWorkout().Questions, Today);

        stats.Acwr.Should().BeGreaterThan(1.5m);
        stats.Insights.Should().Contain(i => i.Level == InsightLevel.Danger && i.Text.Contains("kolano"));
        stats.Insights.Should().Contain(i => i.Text.Contains("ACWR"));
        stats.Tiles.Should().Contain(t => t.Key == SurveyKeys.Rpe);
    }

    [Fact]
    public void Acwr_Needs_Three_Weeks_Of_Data()
    {
        var list = new List<SurveyResponseDto> { Resp(Today.AddDays(-3), 7, 60), Resp(Today.AddDays(-1), 7, 60) };
        SurveyInsights.Acwr(list, Today).Should().BeNull();
    }
}
