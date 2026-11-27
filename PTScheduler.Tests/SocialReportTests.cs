using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class SocialReportTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<EmailService>();
        services.AddScoped<AiService>();
        services.AddScoped<InstagramAi>();
        services.AddScoped<LinkService>();
        services.AddScoped<SocialReportService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Week_data_counts_channels_links_and_todo()
    {
        await using var sp = Build();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var links = sp.GetRequiredService<LinkService>();
        var now = DateTime.UtcNow;
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.InstagramAccounts.Add(new InstagramAccount { Username = "hantly.pl", Followers = 130 });
            ctx.InstagramPosts.AddRange(
                new InstagramPost { Id = 1, Title = "Plan w telefonie", ToInstagram = true, Status = InstagramPostStatus.Published, PublishedAt = now.AddDays(-2) },
                new InstagramPost { Id = 2, Title = "Następny", Status = InstagramPostStatus.Scheduled, ScheduledAt = now.AddDays(2) },
                new InstagramPost { Id = 3, Title = "Do akceptacji", Status = InstagramPostStatus.ToApprove });
            ctx.InstagramMedia.AddRange(
                new InstagramMedia { Id = "m1", Platform = "instagram", PostedAt = now.AddDays(-2), Likes = 40, Comments = 5, PostId = 1, Permalink = "https://instagram.com/p/1" },
                new InstagramMedia { Id = "f1", Platform = "facebook", PostedAt = now.AddDays(-1), Likes = 3 },
                new InstagramMedia { Id = "old", Platform = "instagram", PostedAt = now.AddDays(-20), Likes = 999 });
            ctx.InstagramDaily.AddRange(
                new InstagramDaily { Day = DateOnly.FromDateTime(now.AddDays(-7)), Followers = 120, Reach = 50 },
                new InstagramDaily { Day = DateOnly.FromDateTime(now), Followers = 130, Reach = 70 });
            ctx.SocialChannels.Add(new SocialChannel { Platform = SocialPlatform.Facebook, Api = true, Name = "Hantly", Followers = 300 });
            ctx.InstagramComments.Add(new InstagramComment { Id = "c1", Text = "Fajne", At = now.AddDays(-1) });
            await ctx.SaveChangesAsync();
        }
        var link = await links.ForPostAsync(1, "facebook");
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.Tenants.Add(new Tenant { Slug = "a", Domain = "a", Port = 1, SignupSource = link.Code });
            ctx.TrackedLinkDays.AddRange(
                new TrackedLinkDay { LinkId = link.Id, Day = DateOnly.FromDateTime(InstagramRules.ToWarsaw(now)), Clicks = 12 },
                new TrackedLinkDay { LinkId = link.Id, Day = DateOnly.FromDateTime(InstagramRules.ToWarsaw(now.AddDays(-10))), Clicks = 4 });
            await ctx.SaveChangesAsync();
        }

        var d = await sp.GetRequiredService<SocialReportService>().BuildAsync();
        d.Channels.Select(c => c.Label).Should().Equal("Instagram", "Facebook");
        d.Channels[0].Interactions.Should().Be(45, "stare posty spoza tygodnia się nie liczą");
        d.Channels[0].FollowersDelta.Should().Be(10);
        d.Channels[1].Followers.Should().Be(300);
        d.Top!.Title.Should().Be("Plan w telefonie");
        d.Clicks.Should().Be(12);
        d.ClicksPrev.Should().Be(4);
        d.Signups.Should().Be(1);
        d.ScheduledNext.Should().Be(1);
        d.ToApprove.Should().Be(1);
        d.OpenComments.Should().Be(1);

        var html = SocialReportService.Html(d, ["Facebook przynosi rejestracje.", "<b>x</b>"], "https://hantly.pl");
        html.Should().Contain("Plan w telefonie").And.Contain("(+200%)").And.Contain("&lt;b&gt;x&lt;/b&gt;").And.Contain("https://hantly.pl/panel/social");
        SocialReportService.Subject(d).Should().EndWith("1 rejestracja z linków");
        SocialReportService.Facts(d).Should().Contain("12 kliknięć").And.NotContain("@");
    }

    [Fact]
    public void Week_key_is_iso()
    {
        SocialReportService.WeekKey(new DateTime(2026, 11, 30)).Should().Be("2026-W49");
        SocialReportService.WeekKey(new DateTime(2027, 1, 1)).Should().Be("2026-W53");
    }
}
