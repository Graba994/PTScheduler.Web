using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class TrackedLinkTests
{
    [Fact]
    public void Redirect_keeps_anchor_and_only_local_targets()
    {
        LinkService.Redirect("/register", "abc234").Should().Be("/register?src=abc234");
        LinkService.Redirect("/#cennik", "abc234").Should().Be("/?src=abc234#cennik");
        LinkService.Redirect("/register?plan=pro", "abc234").Should().Be("/register?plan=pro&src=abc234");
        LinkService.SafeTarget("https://zly.pl").Should().Be("/register");
        LinkService.SafeTarget("//zly.pl/x").Should().Be("/register");
        LinkService.SafeTarget("/\\zly.pl").Should().Be("/register");
        LinkService.SafeTarget("/pricing").Should().Be("/pricing");
    }

    [Fact]
    public void Bots_and_link_previews_are_not_clicks()
    {
        LinkService.IsBot("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)").Should().BeTrue();
        LinkService.IsBot("WhatsApp/2.23.20.0 A").Should().BeTrue();
        LinkService.IsBot("Mozilla/5.0 (compatible; Googlebot/2.1)").Should().BeTrue();
        LinkService.IsBot(null).Should().BeTrue();
        LinkService.IsBot("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Instagram 300.0").Should().BeFalse("przeglądarka w Instagramie to człowiek");
        LinkService.IsBot("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36 [FBAN/EMA;FBLC/pl_PL]").Should().BeFalse();
        LinkService.ValidCode("abc234").Should().BeTrue();
        LinkService.ValidCode("../x").Should().BeFalse();
    }

    [Fact]
    public async Task Post_links_per_channel_and_report_counts_signups()
    {
        var (links, db) = Build();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.InstagramPosts.Add(new InstagramPost { Id = 7, Title = "Plan w telefonie" });
            await ctx.SaveChangesAsync();
        }
        var fb = await links.ExpandAsync("Sprawdź: {link} #trener", "facebook", 7);
        var link = await links.ForPostAsync(7, "facebook");
        fb.Should().Be($"Sprawdź: hantly.pl/go/{link.Code} #trener");
        (await links.ForPostAsync(7, "facebook")).Code.Should().Be(link.Code, "jeden link na post i kanał");
        (await links.ExpandAsync("Sprawdź: {link}", "instagram", 7)).Should().Be("Sprawdź: link w bio");
        (await links.ExpandAsync("Bez linku", "threads", 7)).Should().Be("Bez linku");
        (await links.OfPostAsync(7)).Keys.Should().BeEquivalentTo(["facebook"], "Instagram i opis bez {link} nie tworzą linków");
        link.Label.Should().Be("Plan w telefonie");

        var (ad, error) = await links.CreateAsync("Reklama Meta", "reklama", "https://obcy.pl");
        error.Should().BeNull();
        ad!.Target.Should().Be("/register");

        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.Tenants.AddRange(
                new Tenant { Slug = "a", Domain = "a", Port = 1, SignupSource = link.Code, FirstPaidAt = DateTime.UtcNow },
                new Tenant { Slug = "b", Domain = "b", Port = 2, SignupSource = link.Code },
                new Tenant { Slug = "c", Domain = "c", Port = 3, SignupSource = ad.Code },
                new Tenant { Slug = "d", Domain = "d", Port = 4 });
            ctx.TrackedLinkDays.Add(new TrackedLinkDay { LinkId = link.Id, Day = DateOnly.FromDateTime(DateTime.UtcNow), Clicks = 20 });
            await ctx.SaveChangesAsync();
        }
        var r = await links.ReportAsync(30);
        r.Signups.Should().Be(3);
        r.SignupsTotal.Should().Be(4);
        r.Paid.Should().Be(1);
        r.Clicks.Should().Be(20);
        r.Rows[0].Link.Code.Should().Be(link.Code);
        r.Rows[0].PostTitle.Should().Be("Plan w telefonie");
        r.Channels.Select(c => c.Channel).Should().Equal("facebook", "reklama");
    }

    private static (LinkService, IDbContextFactory<PortalDbContext>) Build()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<LinkService>();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<LinkService>(), sp.GetRequiredService<IDbContextFactory<PortalDbContext>>());
    }
}
