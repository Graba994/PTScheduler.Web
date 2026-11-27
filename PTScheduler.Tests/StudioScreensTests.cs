using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Studio;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Studio reklam — robot od zrzutów: trasy, wybór zrzutów dla scenarzysty (Garmin, RODO), obrazy dla AI.</summary>
public partial class StudioScreensTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "PTScheduler.Portal")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Nie znaleziono katalogu repozytorium.");
    }

    [Fact]
    public void RouteCatalog_EveryRouteExistsInTrainerApp()
    {
        var pages = Directory.EnumerateFiles(Path.Combine(Root(), "PTScheduler.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => PageDirective().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        StudioScreens.Routes.Select(r => r.Path).Where(p => !pages.Contains(p)).Should().BeEmpty("robot odwiedza tylko istniejące ekrany");
        StudioScreens.Routes.Select(r => r.Path).Should().OnlyHaveUniqueItems();
        StudioScreens.Routes.Should().OnlyContain(r => r.As == "trainer" || r.As == "client");
        StudioScreens.Routes.Should().Contain(r => r.As == "client", "reklamy pokazują też aplikację klienta");
    }

    [Theory]
    [InlineData("dzis.png", true)]
    [InlineData("clients-id-klient-2.png", true)]
    [InlineData("dzis.jpg", true)]
    [InlineData("../screens.json", false)]
    [InlineData("a/b.png", false)]
    [InlineData(".hidden", false)]
    [InlineData("", false)]
    public void SafeName_BlocksPathTricks(string file, bool ok) => StudioScreens.SafeName(file).Should().Be(ok);

    [Fact]
    public void Usable_SkipsBadScreens_AndGarminWithoutConsent()
    {
        var state = new StudioScreens.State
        {
            Screens =
            [
                new() { File = "dzis.png", Pattern = "/dzis", Good = true },
                new() { File = "okienka.png", Pattern = "/okienka", Good = false },
                new() { File = "my-garmin-klient.png", Pattern = "/my/garmin", As = "client", Good = true },
                new() { File = "my-stats-klient.png", Pattern = "/my/stats", As = "client", Good = true, Feature = "Integracja z Garmin Connect™" },
            ],
        };
        StudioScreens.Usable(state, garmin: false).Select(s => s.File).Should().Equal("dzis.png");
        StudioScreens.Usable(state, garmin: true).Select(s => s.File).Should().Equal("dzis.png", "my-garmin-klient.png", "my-stats-klient.png");
        StudioScreens.Usable(null, garmin: true).Should().BeEmpty();
    }

    [Fact]
    public void ForPrompt_DescribesScreenForScriptwriter()
    {
        var line = StudioScreens.ForPrompt(
        [
            new() { File = "zaleglosci.png", Shows = "Lista 8 osób z zaległościami, razem 7 250 zł", Feature = "Kto mi zalega", W = 780, H = 2532,
                Highlights = [new() { Label = "7 250 zł od 8 osób", Y = 0.12 }, new() { Label = "Przypomnij jednym kliknięciem", Y = 0.5 }] },
            new() { File = "app-klient.png", Shows = "Ekran startowy klienta", As = "client", W = 780, H = 1688 },
        ]);
        line.Should().Be(
            "zaleglosci.png: Lista 8 osób z zaległościami, razem 7 250 zł [Kto mi zalega; widok trenera; wysokość 3.2× szerokości] dymki: „7 250 zł od 8 osób” y=0.12, „Przypomnij jednym kliknięciem” y=0.5\n" +
            "app-klient.png: Ekran startowy klienta [widok klienta; wysokość 2.2× szerokości]");
    }

    [Fact]
    public async Task Ai_WithImages_SendsThemAsImageParts()
    {
        var handler = new CaptureHandler("""{"choices":[{"message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":10,"completion_tokens":2,"cost":0.001}}""");
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddHttpClient();
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<AiService>();
        var sp = services.BuildServiceProvider();
        var settings = sp.GetRequiredService<SiteSettingsService>();
        await settings.SetAsync(SiteSettingsService.Keys.AiEnabled, "true");
        var ai = sp.GetRequiredService<AiService>();
        await ai.SetApiKeyAsync("klucz-testowy");

        var r = await ai.CompleteAsync("studio", null, "system", "opisz", imagesJpegBase64: ["QUJD", "REVG"]);

        r.Text.Should().Be("{\"ok\":true}");
        handler.Body.Should().Contain("\"type\":\"text\",\"text\":\"opisz\"")
            .And.Contain("data:image/jpeg;base64,QUJD").And.Contain("data:image/jpeg;base64,REVG");

        await ai.CompleteAsync("studio", null, "system", "tylko tekst");
        handler.Body.Should().Contain("\"content\":\"tylko tekst\"", "bez obrazów wiadomość zostaje zwykłym tekstem");
    }

    private sealed class CaptureHandler(string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [GeneratedRegex("@page\\s+\"([^\"]+)\"")]
    private static partial Regex PageDirective();
}
