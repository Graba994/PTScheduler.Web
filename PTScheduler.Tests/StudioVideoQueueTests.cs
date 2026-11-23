using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Studio;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Studio reklam, filmy — etap 2: kolejka zadań, głos i dźwięk (klucze, ustawienia głosu, muzyka), wersja renderera.</summary>
public class StudioVideoQueueTests
{
    // ── Kolejka ───────────────────────────────────────────────────────

    [Fact]
    public void RetryAt_TwoAndFourMinutes_ThenStops()
    {
        var now = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);
        StudioJobs.RetryAt(1, now).Should().Be(now.AddMinutes(2));
        StudioJobs.RetryAt(2, now).Should().Be(now.AddMinutes(4));
        StudioJobs.RetryAt(3, now).Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_SameTypeAndVideo_ReturnsExistingJob()
    {
        var (sp, db) = Build(new FakeHandler("render"));
        var jobs = sp.GetRequiredService<StudioJobs>();
        var a = await jobs.EnqueueAsync("render", 7);
        var b = await jobs.EnqueueAsync("render", 7);
        var c = await jobs.EnqueueAsync("render", 8);
        b.Should().Be(a);
        c.Should().NotBe(a);
        (await jobs.IsQueuedAsync("render", 7)).Should().BeTrue();
        (await jobs.IsQueuedAsync("voice", 7)).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessNext_Success_MarksDone()
    {
        var handler = new FakeHandler("sounds");
        var (sp, db) = Build(handler);
        var jobs = sp.GetRequiredService<StudioJobs>();
        await jobs.EnqueueAsync("sounds");

        (await jobs.ProcessNextAsync(CancellationToken.None)).Should().BeTrue();
        (await jobs.ProcessNextAsync(CancellationToken.None)).Should().BeFalse("kolejka jest pusta");

        handler.Calls.Should().Be(1);
        var job = await Single(db);
        job.Status.Should().Be(StudioJobStatus.Done);
        job.Attempts.Should().Be(1);
        job.Error.Should().BeNull();
    }

    [Fact]
    public async Task ProcessNext_Failure_RetriesLater_ThenFailsVideoAfterThirdAttempt()
    {
        var handler = new FakeHandler("render") { Throw = () => new RendererException("Renderer nie odpowiada.") };
        var (sp, db) = Build(handler);
        var jobs = sp.GetRequiredService<StudioJobs>();
        var videoId = await AddVideoAsync(db, StudioVideoStatus.Rendering);
        await jobs.EnqueueAsync("render", videoId);

        await jobs.ProcessNextAsync(CancellationToken.None);
        var job = await Single(db);
        job.Status.Should().Be(StudioJobStatus.Pending);
        job.RunAfter.Should().BeAfter(DateTime.UtcNow.AddMinutes(1));
        (await jobs.ProcessNextAsync(CancellationToken.None)).Should().BeFalse("druga próba dopiero za 2 minuty");

        for (var i = 0; i < 2; i++)
        {
            await MakeDueAsync(db);
            await jobs.ProcessNextAsync(CancellationToken.None);
        }

        job = await Single(db);
        job.Status.Should().Be(StudioJobStatus.Failed);
        job.Attempts.Should().Be(3);
        handler.Calls.Should().Be(3);
        await using var ctx = await db.CreateDbContextAsync();
        var video = await ctx.StudioVideos.SingleAsync();
        video.Status.Should().Be(StudioVideoStatus.Failed);
        video.LastError.Should().Be("Render: Renderer nie odpowiada.");
    }

    [Fact]
    public async Task ProcessNext_FatalError_FailsAtOnce()
    {
        var handler = new FakeHandler("voice") { Throw = () => new StudioJobFatalException("Brak klucza ElevenLabs.") };
        var (sp, db) = Build(handler);
        var jobs = sp.GetRequiredService<StudioJobs>();
        var videoId = await AddVideoAsync(db, StudioVideoStatus.Voicing);
        await jobs.EnqueueAsync("voice", videoId);

        await jobs.ProcessNextAsync(CancellationToken.None);

        (await Single(db)).Status.Should().Be(StudioJobStatus.Failed);
        handler.Calls.Should().Be(1);
        await using var ctx = await db.CreateDbContextAsync();
        (await ctx.StudioVideos.SingleAsync()).LastError.Should().Be("Głos: Brak klucza ElevenLabs.");
    }

    [Fact]
    public async Task ProcessNext_UnknownType_FailsWithoutRetry()
    {
        var (sp, db) = Build();
        var jobs = sp.GetRequiredService<StudioJobs>();
        await jobs.EnqueueAsync("nieznane");
        await jobs.ProcessNextAsync(CancellationToken.None);
        var job = await Single(db);
        job.Status.Should().Be(StudioJobStatus.Failed);
        job.Error.Should().Contain("Nieznany rodzaj");
    }

    // ── Klucze i głos ─────────────────────────────────────────────────

    [Fact]
    public async Task Keys_AreStoredEncrypted_AndReadBack()
    {
        var (sp, _) = Build();
        var media = sp.GetRequiredService<StudioMedia>();
        var settings = sp.GetRequiredService<SiteSettingsService>();

        await media.SaveKeysAsync("azure-tajny-klucz", " WestEurope ", "sk_eleven_tajny");

        (await settings.GetAsync(SiteSettingsService.Keys.StudioAzureKey)).Should().NotContain("azure-tajny-klucz");
        (await settings.GetAsync(SiteSettingsService.Keys.StudioElevenLabsKey)).Should().NotContain("sk_eleven_tajny");
        var c = await media.CredentialsAsync();
        c.AzureKey.Should().Be("azure-tajny-klucz");
        c.AzureRegion.Should().Be("westeurope");
        c.ElevenLabsKey.Should().Be("sk_eleven_tajny");

        await media.SaveKeysAsync(null, "westeurope", ""); // null = bez zmian, pusty = usuń
        c = await media.CredentialsAsync();
        c.AzureKey.Should().Be("azure-tajny-klucz");
        c.ElevenLabsKey.Should().BeNull();
        (await media.KeysStateAsync()).Should().Be(new StudioMedia.KeysState(true, "westeurope", false));
    }

    [Theory]
    [InlineData("Azure", "pl-PL-MarekNeural", "+6%", "+0Hz", null)]
    [InlineData("Azure", "pl-PL-AgnieszkaNeural", "-5%", "-20Hz", null)]
    [InlineData("Azure", "Marek", "+6%", "+0Hz", "Głos Azure")]
    [InlineData("Azure", "pl-PL-MarekNeural", "6", "+0Hz", "Tempo Azure")]
    [InlineData("Azure", "pl-PL-MarekNeural", "+6%", "0", "Wysokość Azure")]
    [InlineData("ElevenLabs", "21m00Tcm4TlvDq8ikWAM", "", "", null)]
    [InlineData("ElevenLabs", "21m00Tcm4TlvDq8ikWAM", "1.05", "", null)]
    [InlineData("ElevenLabs", "21m00Tcm4TlvDq8ikWAM", "+6%", "", "Szybkość ElevenLabs")]
    [InlineData("ElevenLabs", "21m00Tcm4TlvDq8ikWAM", "1.5", "", "Szybkość ElevenLabs")]
    [InlineData("ElevenLabs", "pl-PL-MarekNeural", "", "", "ID głosu ElevenLabs")]
    public void VoiceProblem_ExplainsWrongSettings(string provider, string voiceId, string rate, string pitch, string? problemStart)
    {
        var problem = StudioMedia.VoiceProblem(StudioMedia.Normalize(new StudioMedia.Voice { Provider = provider, VoiceId = voiceId, Rate = rate, Pitch = pitch }));
        if (problemStart is null) problem.Should().BeNull();
        else problem.Should().StartWith(problemStart);
    }

    [Fact]
    public async Task SaveVoice_RejectsBadSettings_AndKeepsGoodOnes()
    {
        var (sp, _) = Build();
        var media = sp.GetRequiredService<StudioMedia>();
        (await media.SaveVoiceAsync(new StudioMedia.Voice { Provider = "elevenlabs", VoiceId = "21m00Tcm4TlvDq8ikWAM", Rate = "1,1" })).Should().BeNull();
        var v = await media.VoiceAsync();
        v.Provider.Should().Be("ElevenLabs");
        v.Rate.Should().Be("1.1");
        (await media.SaveVoiceAsync(new StudioMedia.Voice { Provider = "Azure", VoiceId = "zly" })).Should().NotBeNull();
        (await media.VoiceAsync()).Provider.Should().Be("ElevenLabs", "zły głos nie nadpisał zapisanego");
        (await media.VoiceUnavailableAsync()).Should().Contain("ElevenLabs", "bez klucza nie da się nagrać");
    }

    // ── Dźwięk w filmie ───────────────────────────────────────────────

    [Fact]
    public void SoundFiles_HaveStableNames()
    {
        StudioMedia.FileFor("whoosh").Should().Be("sfx-whoosh.mp3");
        StudioMedia.FileFor("music-upbeat").Should().Be("music-upbeat.mp3");
        StudioMedia.Library.Select(s => s.Key).Should().OnlyHaveUniqueItems()
            .And.Contain(["whoosh", "pop", "ding", "stamp", "sparkle", "confetti"], "te efekty wywołuje kompozycja reklamy (adCues)");
    }

    [Fact]
    public void SoundSection_PicksMusicAndAvailableEffects()
    {
        var all = StudioMedia.Library.Select(s => s.Key).ToHashSet();
        var opts = new StudioMedia.SoundOptions("losowo", 0.1, true, true);

        Json(StudioMedia.SoundSection(opts, all, 0)).Should().Contain("\"music\":\"music-groove.mp3\"");
        Json(StudioMedia.SoundSection(opts, all, 1)).Should().Contain("\"music\":\"music-upbeat.mp3\"");
        Json(StudioMedia.SoundSection(opts with { Music = "brak" }, all, 1)).Should().Contain("\"music\":null");
        Json(StudioMedia.SoundSection(opts with { Music = "music-upbeat" }, new HashSet<string> { "pop" }, 1))
            .Should().Contain("\"music\":null", "brakującego utworu nie wpisujemy").And.Contain("\"sfx\":[\"pop\"]");
        Json(StudioMedia.SoundSection(opts with { Sfx = false }, all, 1)).Should().Contain("\"sfx\":[]");
    }

    // ── Wersja renderera ──────────────────────────────────────────────

    [Fact]
    public void TouchesStudio_OnlyRendererFiles()
    {
        UpdateService.TouchesStudio(["PTScheduler.Portal/Program.cs", "PTScheduler.Tests/X.cs"]).Should().BeFalse();
        UpdateService.TouchesStudio(["PTScheduler.Portal/Program.cs", "studio/src/ad/AdShort.tsx"]).Should().BeTrue();
        UpdateService.TouchesStudio([".dockerignore"]).Should().BeTrue();
        UpdateService.TouchesStudio([]).Should().BeFalse();
    }

    // ── Pomocnicze ────────────────────────────────────────────────────

    private sealed class FakeHandler(string type) : IStudioJobHandler
    {
        public string Type => type;
        public int Calls { get; private set; }
        public Func<Exception>? Throw { get; init; }

        public Task HandleAsync(StudioJob job, CancellationToken ct)
        {
            Calls++;
            return Throw is null ? Task.CompletedTask : Task.FromException(Throw());
        }
    }

    private static (IServiceProvider Sp, IDbContextFactory<PortalDbContext> Db) Build(params IStudioJobHandler[] handlers)
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Studio:DataDir"] = Path.Combine(Path.GetTempPath(), "hantly-studio-test-" + name),
        }).Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<EmailService>();
        services.AddScoped<StudioNotifier>();
        services.AddScoped<StudioMedia>();
        foreach (var h in handlers) services.AddSingleton<IStudioJobHandler>(h);
        services.AddScoped<StudioJobs>();
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<PortalDbContext>>());
    }

    private static async Task<int> AddVideoAsync(IDbContextFactory<PortalDbContext> db, StudioVideoStatus status)
    {
        await using var ctx = await db.CreateDbContextAsync();
        var v = new StudioVideo { Slug = "reklama-1", Status = status };
        ctx.StudioVideos.Add(v);
        await ctx.SaveChangesAsync();
        return v.Id;
    }

    private static async Task<StudioJob> Single(IDbContextFactory<PortalDbContext> db)
    {
        await using var ctx = await db.CreateDbContextAsync();
        return await ctx.StudioJobs.AsNoTracking().SingleAsync();
    }

    private static async Task MakeDueAsync(IDbContextFactory<PortalDbContext> db)
    {
        await using var ctx = await db.CreateDbContextAsync();
        foreach (var j in ctx.StudioJobs) j.RunAfter = DateTime.UtcNow.AddSeconds(-1);
        await ctx.SaveChangesAsync();
    }

    private static string Json(object o) => System.Text.Json.JsonSerializer.Serialize(o);
}
