using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

public class SuspensionReportTests
{
    [Theory]
    [InlineData(null, "Inne")]
    [InlineData("Bez podanego powodu", "Inne")]
    [InlineData("Nieużywane od 21 dni", "Brak aktywności")]
    [InlineData("Koniec okresu próbnego bez płatności", "Okres próbny")]
    [InlineData("Rezygnacja trenera — koniec opłaconego okresu", "Rezygnacja / płatność")]
    [InlineData("Subskrypcja Stripe anulowana (karta albo rezygnacja)", "Rezygnacja / płatność")]
    [InlineData("Ręcznie w Portalu (admin@hantly.pl)", "Ręcznie")]
    public void Kind_groups_reasons(string? detail, string kind) => SuspensionReasons.Kind(detail).Should().Be(kind);

    [Fact]
    public void Reason_texts()
    {
        SuspensionReasons.Inactive(21).Should().Be("Nieużywane od 21 dni");
        SuspensionReasons.Manual(null).Should().Be("Ręcznie w Portalu");
        SuspensionReasons.Manual("anna").Should().Be("Ręcznie w Portalu (anna)");
    }

    [Fact]
    public async Task Report_lists_suspensions_since_last_and_upcoming()
    {
        var (svc, db) = await BuildAsync();
        var now = new DateTime(2026, 11, 6, 7, 0, 0, DateTimeKind.Utc);
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.Plans.AddRange(new Plan { Id = "t-paid", Name = "Płatny", MonthlyPrice = 119 }, new Plan { Id = "t-free", Name = "Darmowy", MonthlyPrice = 0 });
            ctx.Tenants.AddRange(
                new Tenant { Id = 1, Slug = "domobro", CompanyName = "Domobro", PlanId = "t-paid", Status = TenantStatus.Suspended },
                new Tenant { Id = 2, Slug = "stare", CompanyName = "Stare", PlanId = "t-paid", Status = TenantStatus.Suspended },
                // 19 dni bez logowania przy progu 21 — zawieszenie za 2 dni.
                new Tenant { Id = 3, Slug = "cisza", CompanyName = "Cisza", PlanId = "t-paid", Status = TenantStatus.Active, LastActivityAt = now.AddDays(-19), BillingStatus = "active" },
                // Okres ochronny — pomijane.
                new Tenant { Id = 4, Slug = "chronione", CompanyName = "Chronione", PlanId = "t-paid", Status = TenantStatus.Active, LastActivityAt = now.AddDays(-30), GraceUntil = now.AddDays(5), BillingStatus = "active" },
                // Koniec okresu próbnego jutro, bez karty.
                new Tenant { Id = 5, Slug = "proba", CompanyName = "Próba", PlanId = "t-paid", Status = TenantStatus.Active, LastActivityAt = now, TrialEndsAt = now.AddDays(1) },
                // Aktywne, wszystko w porządku.
                new Tenant { Id = 6, Slug = "ok", CompanyName = "OK", PlanId = "t-paid", Status = TenantStatus.Active, LastActivityAt = now.AddDays(-2), BillingStatus = "active" });
            ctx.TenantEvents.AddRange(
                new TenantEvent { TenantId = 1, EventType = TenantEventTypes.Suspended, Detail = SuspensionReasons.Inactive(21), OccurredAt = now.AddHours(-5) },
                new TenantEvent { TenantId = 2, EventType = TenantEventTypes.Suspended, Detail = SuspensionReasons.TrialExpired, OccurredAt = now.AddDays(-3) });
            await ctx.SaveChangesAsync();
        }

        var r = await svc.BuildAsync(now.AddDays(-1), now);

        r.Suspended.Should().ContainSingle().Which.Slug.Should().Be("domobro", "wcześniejsze zawieszenia były w poprzednim raporcie");
        r.Upcoming.Select(u => u.Slug).Should().Equal("proba", "cisza");
        r.TotalSuspended.Should().Be(2);
        var html = svc.Html(r);
        html.Should().Contain("Brak aktywności").And.Contain("Domobro").And.Contain("Nieużywane od 21 dni").And.Contain("/panel/tenants/1");
        html.Should().Contain("Cisza").And.Contain("Brak aktywności od 19 dni").And.Contain("06.11 03:00", "godziny w czasie polskim");
    }

    [Fact]
    public async Task Daily_send_once_and_only_with_content()
    {
        var (svc, db) = await BuildAsync();
        var now = DateTime.UtcNow;
        (await svc.SendDailyAsync(now)).Should().BeFalse("nie ma o czym pisać");
        await using (var ctx = await db.CreateDbContextAsync())
            (await ctx.SiteSettings.AnyAsync(s => s.Key == SiteSettingsService.Keys.SuspensionReportSentAt)).Should().BeTrue("zapisuje, że dziś już sprawdził");
    }

    private static async Task<(SuspensionReportService Svc, IDbContextFactory<PortalDbContext> Db)> BuildAsync()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        await using (var ctx = await db.CreateDbContextAsync()) await ctx.Database.EnsureCreatedAsync();
        var settings = sp.GetRequiredService<SiteSettingsService>();
        var svc = new SuspensionReportService(db, settings, new EmailService(settings, NullLogger<EmailService>.Instance), sp.GetRequiredService<IConfiguration>());
        return (svc, db);
    }
}
