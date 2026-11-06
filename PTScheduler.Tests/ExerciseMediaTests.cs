using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Web.Components.Shared;
using PTScheduler.Web.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Zdjęcia ćwiczeń: Bunny Storage przez Portal (wspólny limit z filmami) i pomocnicze reguły okna ćwiczenia.</summary>
public class ExerciseMediaTests
{
    private const long GB = 1024L * 1024 * 1024;
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x10, 0, 0, 0, .. "WEBPVP8 "u8, 1, 2, 3, 4];

    [Fact]
    public void Image_format_comes_from_file_bytes_not_from_the_name()
    {
        ImageHubService.Extension(Webp).Should().Be("webp");
        ImageHubService.Extension(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1 }).Should().Be("jpg");
        ImageHubService.Extension(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }).Should().Be("png");
        ImageHubService.Extension("<svg onload=alert(1)>"u8).Should().BeNull();
        ImageHubService.Extension([]).Should().BeNull();
    }

    [Fact]
    public void Storage_password_goes_only_to_bunny_storage_hosts()
    {
        ImageHubService.CleanHost("https://Hantly-Media.b-cdn.net/").Should().Be("hantly-media.b-cdn.net");
        ImageHubService.CleanHost("cdn.example.com/../x").Should().BeNull();
        ImageHubService.CleanHost("").Should().BeNull();
        ImageHubService.IsStorageHost("storage.bunnycdn.com").Should().BeTrue();
        ImageHubService.IsStorageHost("uk.storage.bunnycdn.com").Should().BeTrue();
        ImageHubService.IsStorageHost("storage.bunnycdn.com.evil.pl").Should().BeFalse();
        ImageHubService.IsStorageHost("evilstorage.bunnycdn.com").Should().BeFalse();
    }

    [Fact]
    public async Task Photo_lands_in_trainer_folder_counts_toward_shared_quota_and_only_owner_can_delete_it()
    {
        var (sp, http, tenant, other) = await BuildAsync(planGb: 1);
        var images = sp.GetRequiredService<ImageHubService>();
        await images.SaveStorageAsync("hantly-media", "haslo-strefy", null, "https://hantly-media.b-cdn.net/");

        var (url, size, error) = await images.UploadAsync(tenant, "cwiczenia", Webp);

        error.Should().BeNull();
        size.Should().Be(Webp.Length);
        url.Should().MatchRegex(@"^https://hantly-media\.b-cdn\.net/studio/cwiczenia/[0-9a-f]{32}\.webp$");
        var put = http.Requests.Single();
        put.Method.Should().Be(HttpMethod.Put);
        put.RequestUri!.ToString().Should().StartWith("https://storage.bunnycdn.com/hantly-media/studio/cwiczenia/");
        put.Headers.GetValues("AccessKey").Should().Equal("haslo-strefy");

        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            (await VideoHubService.UsedBytesAsync(ctx, tenant.Id)).Should().Be(Webp.Length);
            // Film prawie do pełna — zdjęcie już się nie zmieści (jeden limit na filmy i zdjęcia).
            ctx.TenantVideos.Add(new TenantVideo { TenantId = tenant.Id, VideoGuid = "v1", SizeBytes = GB - Webp.Length - 5 });
            await ctx.SaveChangesAsync();
        }
        var (full, _, fullError) = await images.UploadAsync(tenant, "cwiczenia", Webp);
        full.Should().BeNull();
        fullError.Should().StartWith("Brak miejsca");

        (await images.DeleteAsync(other, url!)).Should().BeFalse("cudze zdjęcie");
        (await images.DeleteAsync(tenant, "https://inny-cdn.pl/studio/cwiczenia/x.webp")).Should().BeFalse();
        (await images.DeleteAsync(tenant, url!)).Should().BeTrue();
        http.Requests.Last().Method.Should().Be(HttpMethod.Delete);
        await using (var ctx = await db.CreateDbContextAsync())
            (await ctx.TenantImages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Upload_is_refused_for_non_images_plans_without_space_and_missing_configuration()
    {
        var (sp, http, tenant, _) = await BuildAsync(planGb: 0);
        var images = sp.GetRequiredService<ImageHubService>();

        (await images.UploadAsync(tenant, "cwiczenia", "GIF89a....."u8.ToArray())).Error.Should().Contain("To nie jest zdjęcie");
        (await images.UploadAsync(tenant, "cwiczenia", Webp)).Error.Should().Contain("nie są jeszcze skonfigurowane");
        await images.SaveStorageAsync("hantly-media", "haslo", null, "hantly-media.b-cdn.net");
        (await images.UploadAsync(tenant, "cwiczenia", Webp)).Error.Should().Contain("nie zawiera miejsca");
        (await images.UploadAsync(tenant, "../etc", Webp)).Error.Should().Contain("Nieznany rodzaj");
        http.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?feature=share&v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/123", null)]
    [InlineData("", null)]
    public void YouTube_link_is_recognised_for_preview(string link, string? id) =>
        ExerciseMediaFields.YouTubeId(link).Should().Be(id);

    [Fact]
    public void Space_is_shown_in_human_units()
    {
        ExerciseMediaService.Size(0).Should().Be("0 MB");
        ExerciseMediaService.Size(11_500_000).Should().Be((11_500_000 / (1024.0 * 1024)).ToString("0.#") + " MB");
        ExerciseMediaService.Size(200 * GB).Should().Be(200.ToString("0.##") + " GB");
        ExerciseMediaService.Size(long.MaxValue).Should().Be("bez limitu");
    }

    private static async Task<(ServiceProvider Sp, RecordingHandler Http, Tenant Tenant, Tenant Other)> BuildAsync(int planGb)
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        var http = new RecordingHandler();
        services.AddSingleton<IHttpClientFactory>(new Factory(http));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<CreditService>();
        services.AddScoped<BunnyPlatformService>();
        services.AddScoped<VideoHubService>();
        services.AddScoped<ImageHubService>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var plan = new Plan { Name = "Pro", VideoProvider = "bunny", MaxVideoStorageGB = planGb };
        var tenant = new Tenant { Slug = "studio", CompanyName = "Studio Siła", Plan = plan };
        var other = new Tenant { Slug = "inne", CompanyName = "Inne", Plan = plan };
        await using (var ctx = await db.CreateDbContextAsync()) { ctx.Tenants.AddRange(tenant, other); await ctx.SaveChangesAsync(); }
        return (sp, http, tenant, other);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Put ? HttpStatusCode.Created : HttpStatusCode.OK));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
