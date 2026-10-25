using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Plan darmowy bez karty: jedna aplikacja na osobę (aliasy adresów) i osobny limit dzienny.</summary>
public class FreePlanGateTests
{
    [Theory]
    [InlineData("O.La.Fit+promo@GoogleMail.com", "olafit@gmail.com")]
    [InlineData("ola.fit@gmail.com", "olafit@gmail.com")]
    [InlineData(" Ola+x@WP.pl ", "ola@wp.pl")]
    [InlineData("ola.fit@wp.pl", "ola.fit@wp.pl")]
    public void NormalizeEmail_UjednolicaAliasy(string input, string expected) =>
        RegistrationGateService.NormalizeEmail(input).Should().Be(expected);

    [Fact]
    public async Task HasAppForPerson_WykrywaAliasGmaila_IPomijaUsuniete()
    {
        var (gate, db, _) = Build();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.Tenants.Add(new Tenant { Slug = "ola", OwnerEmail = "ola.fit@googlemail.com" });
            ctx.Tenants.Add(new Tenant { Slug = "stara", OwnerEmail = "kasia@wp.pl", Status = TenantStatus.Destroyed });
            await ctx.SaveChangesAsync();
        }

        (await gate.HasAppForPersonAsync("olafit+2@gmail.com")).Should().BeTrue();
        (await gate.HasAppForPersonAsync("kasia@wp.pl")).Should().BeFalse();
        (await gate.HasAppForPersonAsync("inna@gmail.com")).Should().BeFalse();
    }

    [Fact]
    public async Task Decide_DarmowyBezKarty_KolejkaPoWyczerpaniuOsobnegoLimitu()
    {
        var (gate, db, settings) = Build();
        await settings.SetAsync(SiteSettingsService.Keys.RegistrationFreeDailyLimit, "2");
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var t = new Tenant { Slug = "a", OwnerEmail = "a@wp.pl" };
            ctx.Tenants.Add(t);
            await ctx.SaveChangesAsync();
            ctx.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.AutoLaunched, Detail = "free" });
            ctx.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.AutoLaunched, Detail = "free" });
            ctx.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = TenantEventTypes.AutoLaunched, Detail = "free", OccurredAt = DateTime.UtcNow.AddDays(-2) });
            await ctx.SaveChangesAsync();
        }

        (await gate.FreeLaunchesTodayAsync()).Should().Be(2);
        (await gate.AutoLaunchesTodayAsync()).Should().Be(0, "starty bez karty nie zjadają limitu planów płatnych");
        var decision = await gate.DecideAsync(new Tenant { Slug = "b", OwnerEmail = "b@wp.pl" }, freeNoCard: true);
        decision.Launch.Should().BeFalse();
        decision.QueueReason.Should().Contain("planu darmowego").And.Contain("(2)");
    }

    [Fact]
    public async Task FreeDailyLimit_DomyslnieTrzy()
    {
        var (gate, _, _) = Build();
        (await gate.FreeDailyLimitAsync()).Should().Be(RegistrationGateService.DefaultFreeDailyLimit).And.Be(3);
    }

    private static (RegistrationGateService Gate, IDbContextFactory<PortalDbContext> Db, SiteSettingsService Settings) Build()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var settings = sp.GetRequiredService<SiteSettingsService>();
        // Docker, raport zasobów i płatności nie są potrzebne: limit zatrzymuje start przed testem serwera.
        var gate = new RegistrationGateService(db, settings, null!, null!, null!, sp.GetRequiredService<IConfiguration>(),
            NullLogger<RegistrationGateService>.Instance);
        return (gate, db, settings);
    }
}
