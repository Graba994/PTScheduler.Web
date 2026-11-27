using System.Text.Json.Nodes;
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

/// <summary>Studio reklam — film 9:16: poprawianie szkicu od AI, wklejanie redakcji, pliki dla renderera, akceptacje.</summary>
public class StudioAdTests
{
    private static AdRules.Draft Draft(params string[] stories) => new()
    {
        Title = "Reklama", Scenes = stories.Select(s => new AdRules.DraftScene { Headline = "Scena", Text = "Tekst.", Story = (JsonObject)JsonNode.Parse(s)! }).ToList(),
    };

    // ── Szkic ─────────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_UnknownScreen_IsReplacedWithUnusedRealOne()
    {
        var d = AdRules.Sanitize(Draft(
            """{"template":"ad-screen","screen":"dzis.png","caption":"A"}""",
            """{"template":"ad-screen","screen":"wymyslony.png","caption":"B","callouts":[{"text":"x","y":0.3}]}""",
            """{"template":"ad-cta","headline":"Zacznij"}"""), ["dzis.png", "zaleglosci.png"], "Zacznij za darmo na hantly.pl");
        AdRules.Str(d.Scenes[0].Story["screen"]).Should().Be("dzis.png");
        AdRules.Str(d.Scenes[1].Story["screen"]).Should().Be("zaleglosci.png");
        d.Scenes[1].Story["callouts"]!.AsArray().Should().BeEmpty("dymki opisywały inny ekran");
    }

    [Fact]
    public void Sanitize_WithoutScreens_PhoneBecomesBenefits_AndLastSceneIsCta()
    {
        var d = AdRules.Sanitize(Draft(
            """{"template":"ad-hook","big":"23:07"}""",
            """{"template":"ad-screen","screen":"dzis.png","caption":"Plan dnia","callouts":[{"text":"Kto dziś","y":0.2}]}""",
            """{"template":"nieznany"}"""), [], "Zacznij za darmo na hantly.pl");
        AdRules.Template(d.Scenes[0].Story).Should().Be("ad-hook");
        AdRules.Template(d.Scenes[1].Story).Should().Be("ad-benefits");
        AdRules.Str(d.Scenes[1].Story["items"]![0]).Should().Be("Kto dziś");
        AdRules.Template(d.Scenes[2].Story).Should().Be("ad-cta");
        AdRules.Str(d.Scenes[2].Story["note"]).Should().Be("Zacznij za darmo na hantly.pl");
    }

    [Fact]
    public void Sanitize_SetsKindsAndNumbers_AndTrimsLabels()
    {
        var d = Draft(
            """{"template":"ad-hook"}""", """{"template":"ad-mascot"}""", """{"template":"ad-benefits"}""", """{"template":"ad-cta"}""");
        d.Scenes[1].Headline = "Bardzo długa etykieta sceny ponad limit";
        d.Scenes[2].Icon = "";
        d = AdRules.Sanitize(d, [], "x");
        d.Scenes.Select(s => s.Kind).Should().Equal("hook", "fact", "fact", "outro");
        d.Scenes.Select(s => s.Number).Should().Equal(null, 1, 2, null);
        d.Scenes[1].Headline.Length.Should().BeLessThanOrEqualTo(22);
        d.Scenes[2].Icon.Should().Be("✨");
    }

    [Fact]
    public void Sanitize_UnknownTemplate_BecomesMascotWithHeadline()
    {
        var d = AdRules.Sanitize(Draft("""{"template":"phone-demo"}""", """{"template":"ad-cta"}"""), [], "x");
        AdRules.Template(d.Scenes[0].Story).Should().Be("ad-mascot");
        AdRules.Str(d.Scenes[0].Story["bubble"]).Should().Be("Scena");
    }

    [Fact]
    public void ParseDraft_AcceptsStoryAsObjectOrString_AndCodeFences()
    {
        const string text = """
            ```json
            {"title":"Hantly 💪","description":"Opis","tags":["trener"],"hooks":["A","B"],"pinnedComment":"?",
             "scenes":[{"kind":"hook","headline":"Hak","icon":"😩","text":"Znowu 23:00?","story":{"template":"ad-hook","big":"23:07"}},
                       {"kind":"outro","headline":"CTA","icon":"🚀","text":"Zacznij.","story":"{\"template\":\"ad-cta\",\"price\":\"0 zł\"}"}]}
            ```
            """;
        var d = AdRules.ParseDraft(text);
        d.Should().NotBeNull();
        d!.Scenes.Should().HaveCount(2);
        AdRules.Str(d.Scenes[0].Story["big"]).Should().Be("23:07");
        AdRules.Str(d.Scenes[1].Story["price"]).Should().Be("0 zł");
        d.Hooks.Should().Equal("A", "B");
        AdRules.ParseDraft("""{"title":"bez scen"}""").Should().BeNull();
        AdRules.ParseDraft("nie json").Should().BeNull();
    }

    // ── Redakcja ──────────────────────────────────────────────────────

    [Fact]
    public void MergeTexts_TakesOnlyPolishTexts_NeverTemplateNumbersOrFiles()
    {
        var original = (JsonObject)JsonNode.Parse("""
            {"template":"ad-screen","screen":"dzis.png","caption":"Zapisy 24/7","scroll":0.4,"side":"left","mood":"happy",
             "callouts":[{"text":"Wolny termin","y":0.2},{"text":"Przypomnienie","y":0.5}]}
            """)!;
        var edited = (JsonObject)JsonNode.Parse("""
            {"template":"ad-cta","screen":"inny.png","caption":"Zapisy *24/7*","scroll":0.9,"side":"right","mood":"sad",
             "callouts":[{"text":"Wolne okienko","y":0.9},{"text":"","y":0.1}],"nowe":"pole"}
            """)!;
        var m = AdRules.MergeTexts(original, edited);
        AdRules.Template(m).Should().Be("ad-screen");
        AdRules.Str(m["screen"]).Should().Be("dzis.png");
        AdRules.Str(m["caption"]).Should().Be("Zapisy *24/7*");
        m["scroll"]!.GetValue<double>().Should().Be(0.4);
        AdRules.Str(m["side"]).Should().Be("left");
        AdRules.Str(m["mood"]).Should().Be("happy");
        AdRules.Str(m["callouts"]![0]!["text"]).Should().Be("Wolne okienko");
        m["callouts"]![0]!["y"]!.GetValue<double>().Should().Be(0.2);
        AdRules.Str(m["callouts"]![1]!["text"]).Should().Be("Przypomnienie", "pusty tekst z redakcji nie kasuje oryginału");
        m.ContainsKey("nowe").Should().BeFalse();
    }

    [Fact]
    public void MergeTexts_ChatMessages_KeepSender()
    {
        var original = (JsonObject)JsonNode.Parse("""{"template":"ad-assistant","messages":[{"from":"ai","text":"Ania nie była od 3 tygodni."},{"from":"me","text":"Napisz"}]}""")!;
        var edited = (JsonObject)JsonNode.Parse("""{"template":"ad-assistant","messages":[{"from":"me","text":"Ani nie było od 3 tygodni."},{"from":"ai","text":"Napisz do niej"}]}""")!;
        var m = AdRules.MergeTexts(original, edited);
        AdRules.Str(m["messages"]![0]!["from"]).Should().Be("ai");
        AdRules.Str(m["messages"]![0]!["text"]).Should().Be("Ani nie było od 3 tygodni.");
        AdRules.Str(m["messages"]![1]!["text"]).Should().Be("Napisz do niej");
    }

    // ── Pliki ─────────────────────────────────────────────────────────

    [Fact]
    public void EpisodeJson_ListsOnlyUsedKnownScreens_ForTheRenderer()
    {
        var v = new StudioVideo { Slug = "reklama-1", Title = "Test" };
        var scenes = new List<StudioScene>
        {
            new() { Order = 0, Kind = "hook", Headline = "Hak", Icon = "😩", Text = "Znowu?", StoryJson = """{"template":"ad-hook","big":"23:07"}""" },
            new() { Order = 1, Kind = "fact", Number = 1, Headline = "Ekran", Icon = "📱", Text = "Patrz.", StoryJson = """{"template":"ad-screen","screen":"dzis.png"}""" },
            new() { Order = 2, Kind = "fact", Number = 2, Headline = "Ekran", Icon = "📱", Text = "I tu.", StoryJson = """{"template":"ad-screen","screen":"my-garmin-klient.png"}""" },
        };
        var sizes = new Dictionary<string, (int W, int H)> { ["dzis.png"] = (780, 2532), ["clients.png"] = (780, 1688) };
        var json = JsonNode.Parse(AdRules.EpisodeJson(v, scenes, "https://hantly.pl", "hantly", sizes, new { music = (string?)null, sfx = Array.Empty<string>() }))!;

        json["format"]!.GetValue<string>().Should().Be("ad");
        json["screensDir"]!.GetValue<string>().Should().Be("hantly");
        json["screens"]!.AsObject().Select(p => p.Key).Should().Equal("dzis.png");
        json["screens"]!["dzis.png"]!["h"]!.GetValue<int>().Should().Be(2532);
        json["scenes"]!.AsArray().Should().HaveCount(3);
        json["scenes"]![1]!["story"]!["template"]!.GetValue<string>().Should().Be("ad-screen", "ujęcie jest obiektem, nie napisem");
        json["theme"]!["accent"]!.GetValue<string>().Should().Be(AdRules.Accent);
    }

    [Fact]
    public void DescriptionTxt_HasEverythingForManualPublishing()
    {
        var v = new StudioVideo { Title = "Koniec z zapisami o 23:00 💪", Description = "Klienci zapisują się sami.", PinnedComment = "Co Cię najbardziej męczy?", Garmin = true };
        var txt = AdRules.DescriptionTxt(v, ["trener personalny", "#shorts"], ["Inny hak"], "https://hantly.pl", garmin: true);
        txt.Should().Contain("TYTUŁ\nKoniec z zapisami o 23:00 💪".Replace("\n", Environment.NewLine))
            .And.Contain("#trenerpersonalny #shorts")
            .And.Contain("KOMENTARZ DO PRZYPIĘCIA").And.Contain("- Inny hak").And.Contain("https://hantly.pl")
            .And.NotContain("Garmin Connect są znakami", "bez Garmina w tekście nie ma dopisku");
    }

    [Fact]
    public void NewSlug_IsSafeForRenderer()
    {
        var slug = AdRules.NewSlug(new DateTime(2026, 11, 24, 9, 5, 0, DateTimeKind.Utc));
        slug.Should().MatchRegex("^reklama-20261124-090500-[0-9a-f]{4}$");
    }

    [Fact]
    public void Length_EstimatesSeconds()
    {
        var (words, seconds) = AdRules.Length(["Znowu wieczór na odpisywaniu klientom?", "Spokojnie, mam to."]);
        words.Should().Be(8);
        seconds.Should().Be(3);
    }

    // ── Akceptacje (baza w pamięci) ───────────────────────────────────

    [Fact]
    public async Task ManualFilm_StartsAtScriptReview_WithTemplateEndingInCta()
    {
        var (sp, db) = Build();
        var videos = sp.GetRequiredService<StudioVideos>();
        var (id, error) = await videos.CreateAsync(1, "zapisy", garmin: false, manual: true);
        error.Should().BeNull();
        var v = await videos.GetAsync(id);
        v!.Status.Should().Be(StudioVideoStatus.ScriptReview);
        v.Scenes.Should().HaveCount(6);
        AdRules.Template((JsonObject)JsonNode.Parse(v.Scenes.Last().StoryJson)!).Should().Be("ad-cta");
        v.Scenes.Should().OnlyContain(s => s.NeedsVoice);
        v.Slug.Should().StartWith("reklama-");
        await using var ctx = await db.CreateDbContextAsync();
        (await ctx.StudioJobs.CountAsync()).Should().Be(0, "ręczny film nie czeka na AI");
    }

    [Fact]
    public async Task SaveScenes_RejectsBadStory_AndMarksOnlyChangedTextForNewVoice()
    {
        var (sp, db) = Build();
        var videos = sp.GetRequiredService<StudioVideos>();
        var (id, _) = await videos.CreateAsync(1, "", false, true);
        await using (var ctx = await db.CreateDbContextAsync())
        {
            foreach (var s in ctx.StudioScenes) { s.NeedsVoice = false; s.SpeechText = s.Text; }
            await ctx.SaveChangesAsync();
        }
        var v = (await videos.GetAsync(id))!;
        StudioVideos.SceneEdit Edit(StudioScene s, string? text = null, string? story = null) => new(s.Id, s.Headline, s.Icon, text ?? s.Text, story ?? s.StoryJson);

        var bad = v.Scenes.Select(s => Edit(s, story: s.Order == 2 ? "{zły json" : null)).ToList();
        (await videos.SaveScenesAsync(id, "T", "", "", bad)).Should().Contain("Scena 3");
        var unknown = v.Scenes.Select(s => Edit(s, story: s.Order == 2 ? """{"template":"ad-video"}""" : null)).ToList();
        (await videos.SaveScenesAsync(id, "T", "", "", unknown)).Should().Contain("nieznane ujęcie");

        var ok = v.Scenes.Select(s => s.Order switch
        {
            1 => Edit(s, text: "Spokojnie, ogarnę to."),
            3 => Edit(s, story: """{"template":"ad-benefits","title":"Nowe","items":["A","B","C"]}"""),
            _ => Edit(s),
        }).ToList();
        (await videos.SaveScenesAsync(id, "Nowy tytuł", "Opis", "Pytanie?", ok)).Should().BeNull();

        v = (await videos.GetAsync(id))!;
        v.Title.Should().Be("Nowy tytuł");
        v.Scenes.Where(s => s.NeedsVoice).Select(s => s.Order).Should().Equal(1);
        v.Scenes.Single(s => s.Order == 1).SpeechText.Should().BeEmpty();
        v.Scenes.Single(s => s.Order == 3).StoryJson.Should().Contain("ad-benefits");
    }

    [Fact]
    public async Task ApproveScript_WithoutVoiceKeys_SaysWhatIsMissing()
    {
        var (sp, _) = Build();
        var videos = sp.GetRequiredService<StudioVideos>();
        var (id, _) = await videos.CreateAsync(1, "", false, true);
        (await videos.ApproveScriptAsync(id)).Should().Contain("Azure");
        (await videos.GetAsync(id))!.Status.Should().Be(StudioVideoStatus.ScriptReview);
    }

    [Fact]
    public async Task ApproveScript_WithVoice_QueuesVoice_AndVideoApprovalNeedsReview()
    {
        var (sp, db) = Build();
        var media = sp.GetRequiredService<StudioMedia>();
        await media.SaveKeysAsync("azure-klucz", "westeurope", null);
        var videos = sp.GetRequiredService<StudioVideos>();
        var (id, _) = await videos.CreateAsync(1, "", false, true);

        (await videos.ApproveVideoAsync(id)).Should().NotBeNull("wideo jeszcze nie ma");
        (await videos.ApproveScriptAsync(id)).Should().BeNull();
        (await videos.GetAsync(id))!.Status.Should().Be(StudioVideoStatus.Voicing);
        await using var ctx = await db.CreateDbContextAsync();
        (await ctx.StudioJobs.SingleAsync()).Type.Should().Be("voice");
        (await videos.RewriteAsync(id, "krócej")).Should().Contain("Poczekaj", "w trakcie nagrywania nie przepisujemy");
    }

    private static (IServiceProvider Sp, IDbContextFactory<PortalDbContext> Db) Build()
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
        services.AddScoped<AiService>();
        services.AddScoped<StudioNotifier>();
        services.AddScoped<StudioMedia>();
        services.AddScoped<StudioJobs>();
        services.AddScoped<RendererClient>();
        // Filmy czytają tylko stan zrzutów — robot (konto demo, instancje trenerów) nie jest tu potrzebny.
        services.AddScoped(p => new StudioScreens(p.GetRequiredService<IDbContextFactory<PortalDbContext>>(), p.GetRequiredService<SiteSettingsService>(),
            null!, null!, null!, p.GetRequiredService<RendererClient>(), p.GetRequiredService<AiService>(), p.GetRequiredService<IConfiguration>(),
            p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StudioScreens>>()));
        services.AddScoped<StudioVideos>();
        var sp = services.BuildServiceProvider();
        return (sp, sp.GetRequiredService<IDbContextFactory<PortalDbContext>>());
    }
}
