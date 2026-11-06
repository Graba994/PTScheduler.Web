using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Infrastructure.Services.Wger;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Katalog z wger.de: mapowanie odpowiedzi /api/v2/exerciseinfo/ (stronicowanie, język PL/EN,
/// partie, sprzęt, wideo, autorzy), ponowny import bez duplikatów oraz przełącznik źródła katalogu.
/// </summary>
public class WgerCatalogTests
{
    private const string Base = "https://wger.test/api/v2/";

    // Dwie strony w kształcie ExerciseInfoSerializer (pola jak w wger/exercises/api/serializers.py).
    private const string Page1 = """
    {"count": 3, "next": "https://wger.test/api/v2/exerciseinfo/?limit=100&offset=100", "previous": null, "results": [
      {"id": 73, "uuid": "aaaa-1", "category": {"id": 11, "name": "Chest"},
       "muscles": [{"id": 4, "name": "Pectoralis major", "name_en": "Chest", "is_front": true}],
       "muscles_secondary": [{"id": 5, "name": "Triceps brachii", "name_en": "Triceps", "is_front": false}, {"id": 4, "name": "Pectoralis major", "name_en": "Chest"}],
       "equipment": [{"id": 8, "name": "Bench"}, {"id": 1, "name": "Barbell"}],
       "license": {"id": 2, "full_name": "Creative Commons Attribution Share Alike 4", "short_name": "CC-BY-SA 4", "url": "https://creativecommons.org/licenses/by-sa/4.0/deed.en"},
       "license_author": "wger.de",
       "images": [
         {"id": 1, "image": "https://wger.test/media/exercise-images/73/bench-2.png", "is_main": false, "license_author": "Everkinetic"},
         {"id": 2, "image": "https://wger.test/media/exercise-images/73/bench-1.png", "is_main": true, "license_author": "Everkinetic"}],
       "translations": [
         {"id": 1, "name": "Bench Press", "description": "<p>Lie on the bench.</p><p>Press the bar&nbsp;up.</p>", "language": 2},
         {"id": 2, "name": "Wyciskanie sztangi na ławce", "description": "<ol><li>Połóż się na ławce.</li><li>Wypchnij sztangę.</li></ol>", "language": 14},
         {"id": 3, "name": "Bankdrücken", "description": "", "language": 1}],
       "videos": [{"id": 9, "video": "https://wger.test/media/exercise-video/73/bench.mp4", "is_main": true, "license_author": "Jan Filmowiec"}],
       "author_history": ["wger.de"], "total_authors_history": ["wger.de", "rolandgeider"]}
    ]}
    """;

    private const string Page2 = """
    {"count": 3, "next": null, "previous": "x", "results": [
      {"id": 9, "uuid": "bbbb-2", "category": {"id": 15, "name": "Cardio"}, "muscles": [], "muscles_secondary": [],
       "equipment": [], "license": {"id": 1, "short_name": "CC-BY-SA 3"}, "images": [], "videos": [],
       "translations": [{"id": 4, "name": "Running", "description": "Run.", "language": 2}],
       "author_history": [], "total_authors_history": []},
      {"id": 10, "uuid": "cccc-3", "category": {"id": 10, "name": "Abs"}, "muscles": [], "muscles_secondary": [],
       "equipment": [], "images": [], "videos": [],
       "translations": [{"id": 5, "name": "Nur Deutsch", "description": "", "language": 1}]}
    ]}
    """;

    private sealed class FakeHandler(Dictionary<string, string> pages) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            return Task.FromResult(pages.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedModules(ModuleSettingsDto dto) : IModuleSettingsService
    {
        public Task<ModuleSettingsDto> GetAsync() => Task.FromResult(dto);
        public Task SaveAsync(ModuleSettingsDto d) => Task.CompletedTask;
    }

    private static (WgerCatalogImporter Importer, FakeHandler Handler) MakeImporter(Microsoft.EntityFrameworkCore.IDbContextFactory<PTScheduler.Infrastructure.Data.ApplicationDbContext> f)
    {
        Environment.SetEnvironmentVariable("WGER_API_BASE_URL", Base);
        var handler = new FakeHandler(new()
        {
            [$"{Base}exerciseinfo/?limit=100&offset=0"] = Page1,
            [$"{Base}exerciseinfo/?limit=100&offset=100"] = Page2
        });
        return (new WgerCatalogImporter(new FakeFactory(handler), f, NullLogger<WgerCatalogImporter>.Instance), handler);
    }

    [Fact]
    public async Task Import_Follows_Pages_And_Maps_Exercises()
    {
        var (f, _) = TestDb.CreateFresh();
        var (importer, handler) = MakeImporter(f);

        var result = await importer.ImportAsync();

        handler.Requests.Should().HaveCount(2);
        result.Should().Be(new WgerImportResult(Added: 2, Updated: 0, Skipped: 1, Total: 3)); // „Nur Deutsch” bez PL/EN

        await using var db = f.CreateDbContext();
        var bench = await db.Exercises.SingleAsync(e => e.SourceKey == "wger:aaaa-1");
        bench.Source.Should().Be(ExerciseSource.Wger);
        bench.Visibility.Should().Be(ExerciseVisibility.Public);
        bench.NamePl.Should().Be("Wyciskanie sztangi na ławce");
        bench.NameEn.Should().Be("Bench Press");
        bench.DescriptionPl.Should().Be("Połóż się na ławce.\nWypchnij sztangę.");
        bench.DescriptionEn.Should().Be("Lie on the bench.\nPress the bar up.");
        bench.PrimaryMuscles.Should().Be("chest");
        bench.SecondaryMuscles.Should().Be("triceps");
        bench.Equipment.Should().Be("barbell"); // sztanga ważniejsza niż ławka
        bench.Tracking.Should().Be(ExerciseTracking.WeightReps);
        bench.ImageUrls.Should().StartWith("https://wger.test/media/exercise-images/73/bench-1.png"); // główne zdjęcie pierwsze
        bench.VideoType.Should().Be(ExerciseVideoType.Url);
        bench.VideoRef.Should().Be("https://wger.test/media/exercise-video/73/bench.mp4");
        bench.Attribution.Should().Contain("CC-BY-SA 4").And.Contain("rolandgeider").And.Contain("Everkinetic").And.Contain("Jan Filmowiec");

        var run = await db.Exercises.SingleAsync(e => e.SourceKey == "wger:bbbb-2");
        run.NamePl.Should().Be("Running"); // brak tłumaczenia PL → nazwa angielska
        run.Category.Should().Be(ExerciseCategory.Cardio);
        run.Tracking.Should().Be(ExerciseTracking.DistanceTime);
        run.VideoType.Should().Be(ExerciseVideoType.None);
    }

    [Fact]
    public async Task Reimport_Updates_In_Place_And_Keeps_Manual_Tracking()
    {
        var (f, _) = TestDb.CreateFresh();
        var (importer, _) = MakeImporter(f);
        await importer.ImportAsync();
        await using (var db = f.CreateDbContext())
        {
            var bench = await db.Exercises.SingleAsync(e => e.SourceKey == "wger:aaaa-1");
            bench.Tracking = ExerciseTracking.Reps;
            bench.NamePl = "stara nazwa";
            await db.SaveChangesAsync();
        }

        var result = await importer.ImportAsync();

        result.Added.Should().Be(0);
        result.Updated.Should().Be(2);
        await using var check = f.CreateDbContext();
        (await check.Exercises.CountAsync(e => e.Source == ExerciseSource.Wger)).Should().Be(2);
        var again = await check.Exercises.SingleAsync(e => e.SourceKey == "wger:aaaa-1");
        again.NamePl.Should().Be("Wyciskanie sztangi na ławce");
        again.Tracking.Should().Be(ExerciseTracking.Reps);
    }

    [Theory]
    [InlineData("<p>Jeden</p>\n<p>Dwa &amp; trzy</p>", "Jeden\nDwa & trzy")]
    [InlineData("Linia<br/>druga", "Linia\ndruga")]
    [InlineData("<p> </p>", null)]
    public void HtmlToText_Keeps_Steps(string html, string? expected) =>
        WgerCatalogImporter.HtmlToText(html).Should().Be(expected);

    private static async Task SeedBothSourcesAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<PTScheduler.Infrastructure.Data.ApplicationDbContext> f, bool withWger)
    {
        await using var db = f.CreateDbContext();
        db.Exercises.Add(new Exercise { Visibility = ExerciseVisibility.Public, NamePl = "Przysiad FED", NameEn = "Squat", SourceKey = "Squat", Equipment = "barbell" });
        if (withWger)
            db.Exercises.Add(new Exercise { Visibility = ExerciseVisibility.Public, Source = ExerciseSource.Wger, NamePl = "Przysiad wger", NameEn = "Squat", SourceKey = "wger:x", Equipment = "dumbbell" });
        db.Exercises.Add(new Exercise { Visibility = ExerciseVisibility.Mine, OwnerTrainerUserId = "t1", NamePl = "Moje", NameEn = "Mine" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Catalog_Shows_Only_Selected_Source_Plus_Own()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedBothSourcesAsync(f, withWger: true);
        var svc = new ExerciseCatalogService(f, TestClock.AtWallClock(new DateTime(2026, 1, 1, 12, 0, 0)),
            new FixedModules(new ModuleSettingsDto { ExerciseCatalogSource = ExerciseSource.Wger }));

        var names = (await svc.SearchAsync("t1", new ExerciseFilterDto())).Select(e => e.NamePl);
        names.Should().BeEquivalentTo(["Przysiad wger", "Moje"]);
        (await svc.GetEquipmentOptionsAsync("t1")).Should().Equal("dumbbell");
    }

    [Fact]
    public async Task Catalog_Falls_Back_To_FreeExerciseDb_Until_Wger_Is_Downloaded()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedBothSourcesAsync(f, withWger: false);
        var svc = new ExerciseCatalogService(f, TestClock.AtWallClock(new DateTime(2026, 1, 1, 12, 0, 0)),
            new FixedModules(new ModuleSettingsDto { ExerciseCatalogSource = ExerciseSource.Wger }));

        var names = (await svc.SearchAsync("t1", new ExerciseFilterDto())).Select(e => e.NamePl);
        names.Should().BeEquivalentTo(["Przysiad FED", "Moje"]);
    }

    [Fact]
    public async Task Exercise_From_Hidden_Source_Stays_Readable_For_Existing_Plans()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedBothSourcesAsync(f, withWger: true);
        int fedId;
        await using (var db = f.CreateDbContext()) fedId = (await db.Exercises.SingleAsync(e => e.SourceKey == "Squat")).Id;
        var svc = new ExerciseCatalogService(f, TestClock.AtWallClock(new DateTime(2026, 1, 1, 12, 0, 0)),
            new FixedModules(new ModuleSettingsDto { ExerciseCatalogSource = ExerciseSource.Wger }));

        (await svc.GetAsync("t1", fedId)).Should().NotBeNull();
    }
}
