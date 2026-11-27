using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Application.Wearables;
using PTScheduler.Domain.Enums;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Plan na zegarek: dopasowanie ćwiczeń do katalogu Garmina, budowa treningu (Training API V2) i wysyłka przez Portal.</summary>
public class GarminWorkoutTests
{
    [Theory]
    [InlineData("Barbell Bench Press - Medium Grip", null, "BENCH_PRESS", "BARBELL_BENCH_PRESS")]
    [InlineData("Pushups", null, "PUSH_UP", "PUSH_UP")]
    [InlineData("Barbell Deadlift", null, "DEADLIFT", "BARBELL_DEADLIFT")]
    [InlineData("Dumbbell Lateral Raise", null, "LATERAL_RAISE", "DUMBBELL_LATERAL_RAISE")]
    [InlineData("Barbell Step Ups", null, "SQUAT", "BARBELL_STEP_UP")]
    [InlineData("Wyciskanie sztangi leżąc", "Wyciskanie sztangi leżąc", "BENCH_PRESS", "BARBELL_BENCH_PRESS")]
    [InlineData("Martwy ciąg", "Martwy ciąg", "DEADLIFT", "BARBELL_DEADLIFT")]
    public void Exercises_map_to_garmin_catalog(string en, string? pl, string category, string name)
    {
        GarminExerciseMap.Count.Should().BeGreaterThan(1400);
        GarminExerciseMap.Find(en, pl).Should().Be(new GarminExerciseMap.Match(category, name));
    }

    [Fact]
    public void Unknown_exercise_has_no_match()
    {
        GarminExerciseMap.Find("Balance Board").Should().BeNull();
        GarminExerciseMap.Find("Seated", "Siedząc").Should().BeNull("same słowa o ustawieniu nie mówią, jakie to ćwiczenie");
    }

    [Fact]
    public void Plan_day_becomes_strength_workout_with_sets_rest_and_weight()
    {
        var r = GarminWorkoutBuilder.Build("Siła A · Dzień A", "Plan od trenera.",
        [
            new("Barbell Bench Press - Medium Grip", "Wyciskanie sztangi leżąc", ExerciseTracking.WeightReps, 3, "8-10", 60m, null, null, 90, null),
            new("Balance Board", "Równowaga na poduszce", ExerciseTracking.Time, 1, null, null, 45, null, null, "spokojnie"),
        ]);

        r.Error.Should().BeNull();
        r.Steps.Should().Be(4);
        r.Unmatched.Should().Equal("Równowaga na poduszce");
        var w = r.Workout!;
        w["sport"]!.GetValue<string>().Should().Be("STRENGTH_TRAINING");
        w["workoutProvider"]!.GetValue<string>().Should().Be("Hantly");
        var steps = w["segments"]![0]!["steps"]!.AsArray();
        steps.Should().HaveCount(2);

        var repeat = steps[0]!;
        repeat["type"]!.GetValue<string>().Should().Be("WorkoutRepeatStep");
        repeat["stepOrder"]!.GetValue<int>().Should().Be(1);
        repeat["repeatType"]!.GetValue<string>().Should().Be("REPEAT_UNTIL_STEPS_CMPLT");
        repeat["repeatValue"]!.GetValue<int>().Should().Be(3);
        var press = repeat["steps"]![0]!;
        press["stepOrder"]!.GetValue<int>().Should().Be(2);
        press["durationType"]!.GetValue<string>().Should().Be("REPS");
        press["durationValue"]!.GetValue<double>().Should().Be(8);
        press["weightValue"]!.GetValue<double>().Should().Be(60);
        press["weightDisplayUnit"]!.GetValue<string>().Should().Be("KILOGRAM");
        press["exerciseCategory"]!.GetValue<string>().Should().Be("BENCH_PRESS");
        press["description"]!.GetValue<string>().Should().Be("Wyciskanie sztangi leżąc · 8-10");
        var rest = repeat["steps"]![1]!;
        rest["intensity"]!.GetValue<string>().Should().Be("REST");
        rest["durationType"]!.GetValue<string>().Should().Be("TIME");
        rest["durationValue"]!.GetValue<int>().Should().Be(90);

        var board = steps[1]!;
        board["stepOrder"]!.GetValue<int>().Should().Be(4);
        board["durationType"]!.GetValue<string>().Should().Be("TIME");
        board["exerciseCategory"].Should().BeNull();
        board["description"]!.GetValue<string>().Should().Be("Równowaga na poduszce · spokojnie");
    }

    [Fact]
    public void Too_many_steps_and_empty_day_are_refused()
    {
        var many = Enumerable.Range(0, 40).Select(_ => new GarminWorkoutBuilder.Item("Pushups", "Pompki", ExerciseTracking.Reps, 3, "10", null, null, null, 60, null)).ToList();
        GarminWorkoutBuilder.Build("Dużo", null, many).Error.Should().Contain("Za dużo kroków");
        GarminWorkoutBuilder.Build("Pusto", null, []).Error.Should().Contain("nie ma ćwiczeń");
        GarminWorkoutBuilder.Duration(new("Pushups", "Pompki", ExerciseTracking.Reps, 3, "max", null, null, null, null, null)).Type.Should().Be("OPEN");
    }

    [Fact]
    public async Task Portal_creates_workout_schedules_it_and_replaces_previous()
    {
        var (garmin, http, tenant) = await BuildAsync();
        http.Respond = req => req.Method == HttpMethod.Post && req.RequestUri!.ToString() == GarminService.WorkoutCreateUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"workoutId":555,"workoutName":"x"}""") }
            : req.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("777") }
            : new HttpResponseMessage(HttpStatusCode.NoContent);
        var workout = JsonDocument.Parse("""{"workoutName":"Siła A","sport":"STRENGTH_TRAINING","segments":[]}""").RootElement;

        var r = await garmin.PushWorkoutAsync(tenant, 7, workout, new DateOnly(2026, 11, 2), replaceWorkoutId: 111);

        r.Should().Be(new GarminService.WorkoutResult(555, 777, null));
        http.Calls.Select(c => $"{c.Method} {c.Url}").Should().Equal(
            $"DELETE {GarminService.WorkoutUrl}111",
            $"POST {GarminService.WorkoutCreateUrl}",
            $"POST {GarminService.ScheduleUrl}");
        http.Calls.Should().OnlyContain(c => c.Auth == "Bearer tok-123");
        http.Calls[1].Body.Should().Contain("STRENGTH_TRAINING");
        JsonNode.Parse(http.Calls[2].Body!)!.ToJsonString().Should().Be("""{"workoutId":555,"date":"2026-11-02"}""");
    }

    [Fact]
    public async Task Portal_explains_missing_permission_and_respects_switch()
    {
        var (garmin, http, tenant) = await BuildAsync();
        http.Respond = _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        var workout = JsonDocument.Parse("{}").RootElement;
        (await garmin.PushWorkoutAsync(tenant, 7, workout, new DateOnly(2026, 11, 2), null)).Error.Should().Contain("nie zezwolił na import treningów");

        await garmin.SetTrainingAsync(false);
        (await garmin.PushWorkoutAsync(tenant, 7, workout, new DateOnly(2026, 11, 2), null)).Error.Should().Contain("wyłączone");
        (await garmin.PushWorkoutAsync(tenant, 99, workout, new DateOnly(2026, 11, 2), null)).Error.Should().NotBeNull();
        GarminService.ReadId("""{"scheduleId":12}""", "scheduleId").Should().Be(12);
        GarminService.ReadId("nie-json", "x").Should().BeNull();
    }

    private static async Task<(GarminService Garmin, FakeGarmin Http, Tenant Tenant)> BuildAsync()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        var dp = new EphemeralDataProtectionProvider();
        services.AddSingleton<IDataProtectionProvider>(dp);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var settings = sp.GetRequiredService<SiteSettingsService>();
        var config = sp.GetRequiredService<IConfiguration>();
        var tenants = new TenantService(db, null!, settings, null!, null!, null!, null!, config, NullLogger<TenantService>.Instance);
        var garmin = new GarminService(settings, db, dp, config, tenants, NullLogger<GarminService>.Instance);
        var http = new FakeGarmin();
        garmin.TrainingHttp = new HttpMessageInvoker(http);
        await garmin.SaveConfigAsync("client-id", "secret");
        await settings.SetAsync(SiteSettingsService.Keys.GarminEnabled, "true");
        await garmin.SetTrainingAsync(true);

        var tenant = new Tenant { Slug = "studio", CompanyName = "Studio" };
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
            ctx.GarminConnections.Add(new GarminConnection
            {
                TenantId = tenant.Id, TenantClientId = 7, GarminUserId = "g-1",
                AccessTokenProtected = dp.CreateProtector("garmin-secrets").Protect("tok-123"), AccessTokenExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await ctx.SaveChangesAsync();
        }
        return (garmin, http, tenant);
    }

    private sealed class FakeGarmin : HttpMessageHandler
    {
        public sealed record Call(HttpMethod Method, string Url, string? Auth, string? Body);
        public List<Call> Calls { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add(new(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return Respond(request);
        }
    }
}
