using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Application.Wearables;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Tests.Helpers;
using PTScheduler.Web.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Zegarek Garmin: dane z Garmin Connect, gotowość na dziś i dopięcie aktywności do wizyty.</summary>
public class GarminTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Parser_reads_dailies_sleep_hrv_and_activity()
    {
        var daily = GarminParser.Day("dailies", Json("""{"userId":"u","calendarDate":"2026-10-27","steps":8123,"restingHeartRateInBeatsPerMinute":54,"averageStressLevel":-1,"bodyBatteryHighestValue":81,"activeKilocalories":512.4}"""))!;
        daily.Date.Should().Be(new DateOnly(2026, 10, 27));
        daily.Steps.Should().Be(8123);
        daily.RestingHeartRate.Should().Be(54);
        daily.AverageStress.Should().BeNull("-1 = brak pomiaru u Garmina");
        daily.BodyBatteryHigh.Should().Be(81);
        daily.ActiveKilocalories.Should().Be(512);

        var sleep = GarminParser.Day("sleeps", Json("""{"calendarDate":"2026-10-27","durationInSeconds":25200,"deepSleepDurationInSeconds":5400,"remSleepInSeconds":4800,"overallSleepScore":{"value":78,"qualifierKey":"GOOD"}}"""))!;
        sleep.SleepMinutes.Should().Be(420);
        sleep.DeepSleepMinutes.Should().Be(90);
        sleep.SleepScore.Should().Be(78);

        GarminParser.Day("hrv", Json("""{"calendarDate":"2026-10-27","lastNightAvg":52}"""))!.HrvLastNight.Should().Be(52);
        GarminParser.Day("dailies", Json("""{"steps":1}""")).Should().BeNull("bez daty dnia nie zapisujemy");

        var act = GarminParser.ActivityOf(Json("""{"summaryId":"a1","activityType":"RUNNING","startTimeInSeconds":1793095200,"durationInSeconds":2400,"averageHeartRateInBeatsPerMinute":148,"distanceInMeters":6200.5,"deviceName":"Forerunner 265"}"""))!;
        act.Name.Should().Be("Bieg");
        act.StartUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1793095200).UtcDateTime);
        act.DistanceMeters.Should().Be(6201);
        act.DeviceName.Should().Be("Forerunner 265");
        GarminParser.TypeName("INDOOR_CLIMBING").Should().Be("Indoor climbing");
    }

    private static List<ReadinessRules.Day> History(DateOnly today, int days = 14) =>
        Enumerable.Range(1, days).Select(i => new ReadinessRules.Day(today.AddDays(-i), 450, 80, 54, 52, 80)).ToList();

    [Fact]
    public void Readiness_low_after_short_night_high_rhr_and_low_hrv()
    {
        var today = new DateOnly(2026, 10, 27);
        var h = History(today);
        h.Add(new ReadinessRules.Day(today, 310, 52, 61, 38, 34));
        var r = ReadinessRules.For(today, h);
        r.Level.Should().Be(ReadinessRules.Level.Low);
        r.Reasons.Should().Contain(x => x.StartsWith("krótki sen (5 h 10 min)")).And.Contain(x => x.Contains("tętno spoczynkowe +7"))
            .And.Contain(x => x.StartsWith("HRV poniżej normy")).And.Contain("niska Body Battery (34)");
        r.Suggestion.Should().Contain("lżejszy");
    }

    [Fact]
    public void Readiness_good_on_normal_day_and_unknown_without_fresh_data()
    {
        var today = new DateOnly(2026, 10, 27);
        var h = History(today);
        h.Add(new ReadinessRules.Day(today, 455, 82, 54, 53, 85));
        ReadinessRules.For(today, h).Level.Should().Be(ReadinessRules.Level.Good);

        ReadinessRules.For(today, History(today.AddDays(-5))).Level.Should().Be(ReadinessRules.Level.Unknown, "dane sprzed kilku dni nie mówią nic o dziś");
        // Bez normy (za mało dni) tętno i HRV nie obniżają wyniku — liczy się sam sen i Body Battery.
        ReadinessRules.For(today, [new(today, 455, 80, 70, 20, 80)]).Level.Should().Be(ReadinessRules.Level.Good);
    }

    [Fact]
    public void Activity_matches_the_overlapping_session()
    {
        var day = new DateTime(2026, 10, 27);
        var sessions = new[] { (1, day.AddHours(7), day.AddHours(8)), (2, day.AddHours(18), day.AddHours(19)) };
        ReadinessRules.MatchSession(day.AddHours(7).AddMinutes(5), 3300, sessions).Should().Be(1);
        ReadinessRules.MatchSession(day.AddHours(12), 3600, sessions).Should().BeNull();
        ReadinessRules.MatchSession(day.AddHours(18).AddMinutes(50), 3600, sessions).Should().BeNull("tylko 10 minut wspólnych z godzinną wizytą");
    }

    private static async Task<(WearableService Svc, IDbContextFactory<ApplicationDbContext> Db)> BuildAsync(bool connected = true)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"garmin_{Guid.NewGuid():N}").Options;
        var factory = new TestDbFactory(options);
        await using (var db = factory.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 7, ApplicationUserId = "u7", FirstName = "Marta", TrainerUserId = "t1" });
            db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
            db.Sessions.Add(new Session { Id = 70, ClientId = 7, SessionTypeId = 1, TrainerUserId = "t1", StartTime = new DateTime(2026, 10, 27, 8, 0, 0), Status = SessionStatus.Scheduled });
            db.WearableLinks.Add(new WearableLink { ClientId = 7, ConnectedAt = connected ? DateTime.UtcNow : null });
            await db.SaveChangesAsync();
        }
        return (new WearableService(factory, TestClock.AtWallClock(new DateTime(2026, 10, 27, 12, 0, 0)), NullLogger<WearableService>.Instance), factory);
    }

    [Fact]
    public async Task Ingest_merges_day_parts_and_links_activity_to_session()
    {
        var (svc, factory) = await BuildAsync();
        (await svc.IngestAsync(7, "dailies", Json("""[{"userId":"u","calendarDate":"2026-10-27","steps":9000,"restingHeartRateInBeatsPerMinute":55}]"""))).Should().Be(1);
        (await svc.IngestAsync(7, "sleeps", Json("""[{"userId":"u","calendarDate":"2026-10-27","durationInSeconds":27000}]"""))).Should().Be(1);
        // 08:05 czasu warszawskiego = 07:05 UTC (zimowy, 27.10.2026)
        var start = new DateTimeOffset(2026, 10, 27, 7, 5, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        (await svc.IngestAsync(7, "activities", Json($$"""[{"summaryId":"x1","activityType":"STRENGTH_TRAINING","startTimeInSeconds":{{start}},"durationInSeconds":3300,"averageHeartRateInBeatsPerMinute":118,"deviceName":"fenix 8"}]"""))).Should().Be(1);
        // Ponowne dostarczenie tej samej aktywności nie dubluje wiersza.
        await svc.IngestAsync(7, "activities", Json($$"""[{"summaryId":"x1","activityType":"STRENGTH_TRAINING","startTimeInSeconds":{{start}},"durationInSeconds":3300,"averageHeartRateInBeatsPerMinute":120}]"""));

        await using var db = factory.CreateDbContext();
        var day = await db.WearableDays.SingleAsync();
        day.Steps.Should().Be(9000);
        day.SleepMinutes.Should().Be(450);
        var act = await db.WearableActivities.SingleAsync();
        act.SessionId.Should().Be(70);
        act.AverageHeartRate.Should().Be(120);
        (await db.WearableLinks.SingleAsync()).DeviceName.Should().Be("fenix 8");

        await svc.DeleteDataAsync(7);
        (await db.WearableDays.CountAsync()).Should().Be(0);
        (await db.WearableActivities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Nothing_is_saved_without_connected_watch()
    {
        var (svc, factory) = await BuildAsync(connected: false);
        (await svc.IngestAsync(7, "dailies", Json("""[{"calendarDate":"2026-10-27","steps":9000}]"""))).Should().Be(0);
        await using var db = factory.CreateDbContext();
        (await db.WearableDays.CountAsync()).Should().Be(0, "bez zgody i połączenia nie zapisujemy danych");
    }

    [Fact]
    public void Pkce_challenge_and_safe_return_path()
    {
        // Przykład z RFC 7636 (dodatek B).
        PTScheduler.Portal.Services.GarminService.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")
            .Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
        PTScheduler.Portal.Services.GarminService.SafePath("/my/garmin").Should().Be("/my/garmin");
        PTScheduler.Portal.Services.GarminService.SafePath("https://zly.pl/x").Should().Be("/garmin");
        PTScheduler.Portal.Services.GarminService.SafePath("//zly.pl").Should().Be("/garmin");
    }

    [Fact]
    public void Wearables_entitlement_needs_plan_flag_and_global_switch()
    {
        static bool Wearables(string json) => JsonDocument.Parse(json).RootElement.GetProperty("wearables").GetBoolean();
        var pro = new PTScheduler.Portal.Entities.Plan { Id = "pro", Name = "Pro", Wearables = true };
        var start = new PTScheduler.Portal.Entities.Plan { Id = "start", Name = "Start", Wearables = false };
        var none = PTScheduler.Portal.Services.ExtraLimits.None;
        Wearables(PTScheduler.Portal.Services.TenantService.SerializeEntitlements(pro, none, garminOn: true)).Should().BeTrue();
        Wearables(PTScheduler.Portal.Services.TenantService.SerializeEntitlements(pro, none, garminOn: false)).Should().BeFalse("wyłączony globalnie — klienci nic nie widzą");
        Wearables(PTScheduler.Portal.Services.TenantService.SerializeEntitlements(start, none, garminOn: true)).Should().BeFalse("plan bez zegarków");

        // Aplikacja studia czyta flagę z JSON-a uprawnień (camelCase) i ukrywa Garmina, gdy jej nie ma.
        var ent = JsonSerializer.Deserialize<PTScheduler.Web.Services.Entitlements>(
            PTScheduler.Portal.Services.TenantService.SerializeEntitlements(pro, none, garminOn: true),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        ent.Wearables.Should().BeTrue();
    }

    [Fact]
    public void Readiness_line_for_trainer()
    {
        var low = new ReadinessRules.Result(ReadinessRules.Level.Low, 30, ["krótki sen (5 h 10 min)", "HRV poniżej normy (38 ms, zwykle 52)", "niska Body Battery (34)"], "");
        WearableService.Line(low).Should().Be("Garmin: gotowość niska — krótki sen (5 h 10 min), HRV poniżej normy (38 ms, zwykle 52)");
        WearableService.Line(low, 1).Should().Be("Garmin: gotowość niska — krótki sen (5 h 10 min)");
        WearableService.Line(new ReadinessRules.Result(ReadinessRules.Level.Good, 90, ["Body Battery 54"], "")).Should().Be("Garmin: gotowość dobra");
    }

    [Fact]
    public void Portal_keeps_only_supported_garmin_data_and_drops_gps_details()
    {
        var (payload, kinds) = PTScheduler.Portal.Services.GarminService.Filter(
            """{"dailies":[{"userId":"u1","steps":100}],"activityDetails":[{"samples":[{"latitudeInDegree":50.29}]}],"epochs":[]}""");
        kinds.Should().Be("dailies");
        payload.Should().Contain("\"steps\":100").And.NotContain("latitude").And.NotContain("activityDetails");

        PTScheduler.Portal.Services.GarminService.Filter("""{"activityDetails":[{"samples":[]}]}""").Payload.Should().BeNull();
        PTScheduler.Portal.Services.GarminService.Filter("nie-json").Payload.Should().BeNull();
        PTScheduler.Portal.Services.GarminService.Filter("""{"deregistrations":[{"userId":"u1"}]}""").Kinds.Should().Be("deregistrations");
    }

    [Fact]
    public async Task Ai_consent_is_separate_switchable_and_withdrawn_with_disconnect()
    {
        var (svc, db) = await BuildAsync();
        (await svc.LinkAsync(7))!.AiConsentAt.Should().BeNull("bez osobnej zgody dane z zegarka nie idą do AI");

        await svc.SetAiConsentAsync(7, true);
        (await svc.LinkAsync(7))!.AiConsentAt.Should().NotBeNull();
        await svc.SetAiConsentAsync(7, false);
        (await svc.LinkAsync(7))!.AiConsentAt.Should().BeNull();

        await svc.SetAiConsentAsync(7, true);
        await svc.SetLinkAsync(7, false, null);
        var link = await svc.LinkAsync(7);
        link!.IsConnected.Should().BeFalse();
        link.AiConsentAt.Should().BeNull("odłączenie zegarka wycofuje też zgodę na AI");
    }

    [Fact]
    public void Week_report_compares_with_previous_week_and_reads_well()
    {
        var mon = new DateOnly(2026, 10, 19);
        WearableWeekRules.LastFullWeekStart(new DateOnly(2026, 10, 28)).Should().Be(mon); // środa → poprzedni pon.
        WearableWeekRules.LastFullWeekStart(new DateOnly(2026, 10, 26)).Should().Be(mon); // poniedziałek → tydzień, który się skończył

        var days = new List<WearableDay>();
        for (var i = 0; i < 7; i++)
        {
            days.Add(new WearableDay { Date = mon.AddDays(i), SleepMinutes = 425, Steps = 7500, RestingHeartRate = 58 });
            days.Add(new WearableDay { Date = mon.AddDays(i - 7), SleepMinutes = 405, Steps = 6000, RestingHeartRate = 60 });
        }
        var acts = new[] { new WearableWeekRules.Act(mon.AddDays(1), 3600, true), new WearableWeekRules.Act(mon.AddDays(3), 2700, false),
                           new WearableWeekRules.Act(mon.AddDays(-2), 3000, false) };

        var w = WearableWeekRules.For(mon, days, acts);

        w.DaysWithData.Should().Be(7);
        w.Activities.Should().Be(2);
        w.SessionsWithWatch.Should().Be(1);
        WearableWeekRules.Text(w).Should().Be($"Sen śr. 7 h 5 min (+20 min) · {52500.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"))} kroków · 2 aktywności (1 h 45 min) · tętno spocz. 58 (−2)");
        WearableWeekRules.Text(WearableWeekRules.For(mon, [], [])).Should().Be("Brak danych z zegarka w tym tygodniu.");
        WearableWeekRules.SessionLine(3120, 430, 138, 171, 5240).Should().Be("52 min · 430 kcal · śr. tętno 138 · max 171 · 5,2 km");
        ReadinessRules.Duration(45).Should().Be("45 min");
    }

    [Fact]
    public async Task Session_summary_goes_once_only_when_studio_and_client_allow_it()
    {
        var (svc, db) = await BuildAsync();
        await using (var ctx = db.CreateDbContext())
        {
            ctx.WearableActivities.Add(new WearableActivity { ClientId = 7, ExternalId = "a1", Name = "Trening", StartUtc = DateTime.UtcNow.AddHours(-2), DurationSeconds = 3000, Kilocalories = 400, SessionId = 70 });
            await ctx.SaveChangesAsync();
        }
        var modules = new Moq.Mock<PTScheduler.Application.Interfaces.IModuleSettingsService>();
        var settings = new PTScheduler.Application.DTOs.ModuleSettingsDto();
        modules.Setup(m => m.GetAsync()).Returns(() => Task.FromResult(settings));
        var push = new Moq.Mock<PTScheduler.Application.Interfaces.IWebPushService>();
        var ent = new EntitlementService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), db, NullLogger<EntitlementService>.Instance);
        ent.ReplaceFromJson("""{"name":"Pro","wearables":true}""");
        var insights = new WearableInsightsService(db, modules.Object, push.Object, ent, TestClock.AtWallClock(new DateTime(2026, 10, 27, 12, 0, 0)), NullLogger<WearableInsightsService>.Instance);

        (await insights.NotifySessionSummariesAsync(7)).Should().Be(0, "studio nie włączyło podsumowań");
        settings.WearableSessionSummaryEnabled = true;
        await insights.SetClientNotificationsAsync(7, sessions: false, weekly: true);
        (await insights.NotifySessionSummariesAsync(7)).Should().Be(0, "klient wyłączył powiadomienia");
        await insights.SetClientNotificationsAsync(7, sessions: true, weekly: true);
        (await insights.NotifySessionSummariesAsync(7)).Should().Be(1);
        (await insights.NotifySessionSummariesAsync(7)).Should().Be(0, "każdy trening tylko raz");
        push.Verify(p => p.SendAsync("u7", Moq.It.Is<PTScheduler.Application.DTOs.PushMessageDto>(m => m.Body.StartsWith("50 min · 400 kcal") && m.Url == "/my/garmin")), Moq.Times.Once);
    }

    private sealed class TestDbFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
