using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>„Zgłoś błąd”: zapis zgłoszenia w Portalu, zrzut ekranu, limit dzienny, stany i sprzątanie starych zrzutów.</summary>
public class BugReportTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private static BugReportService.Incoming Body(string? comment = "Nie zapisuje się wizyta", string? shot = null) =>
        new(comment, "/calendar?view=week", "Kalendarz", "jan@demo.pl", "Jan Kowalski", "trener",
            "Mozilla/5.0 (iPhone)", "390×844 @3x", "abc1234", "12:00:01 error: boom", shot, "image/jpeg");

    [Fact]
    public async Task Saves_comment_context_and_screenshot_from_data_url()
    {
        var (bugs, db, tenant) = await BuildAsync();
        var (ok, error, id) = await bugs.SaveAsync(tenant, Body("  Nie zapisuje się wizyta  ", "data:image/jpeg;base64," + Convert.ToBase64String(Jpeg)));

        ok.Should().BeTrue(error);
        var row = await bugs.GetAsync(id);
        row!.Comment.Should().Be("Nie zapisuje się wizyta");
        row.HasScreenshot.Should().BeTrue();
        row.Status.Should().Be(BugReportStatus.New);
        row.PageUrl.Should().Be("/calendar?view=week");
        (await bugs.ScreenshotAsync(id))!.Value.Data.Should().Equal(Jpeg);
        await using var ctx = await db.CreateDbContextAsync();
        (await ctx.BugReports.SingleAsync()).TenantId.Should().Be(tenant.Id);
    }

    [Fact]
    public async Task Rejects_empty_comment_broken_or_oversized_screenshot()
    {
        var (bugs, _, tenant) = await BuildAsync();
        (await bugs.SaveAsync(tenant, Body("   "))).Ok.Should().BeFalse();
        (await bugs.SaveAsync(tenant, Body(shot: "to-nie-base64!"))).Error.Should().Be("Zrzut ekranu jest uszkodzony.");
        var big = Convert.ToBase64String(new byte[BugReportService.MaxScreenshotBytes + 1]);
        (await bugs.SaveAsync(tenant, Body(shot: big))).Error.Should().Be("Zrzut ekranu jest za duży.");
        (await bugs.SaveAsync(tenant, Body(comment: new string('x', 5000)))).Ok.Should().BeTrue("za długi opis jest przycinany, nie odrzucany");
    }

    [Fact]
    public async Task Limits_reports_per_tenant_per_day()
    {
        var (bugs, _, tenant) = await BuildAsync();
        for (var i = 0; i < BugReportService.MaxPerTenantPerDay; i++)
            (await bugs.SaveAsync(tenant, Body($"Błąd {i}"))).Ok.Should().BeTrue();
        (await bugs.SaveAsync(tenant, Body("Jeszcze jeden"))).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Status_changes_set_and_clear_closed_date_and_old_screenshots_are_dropped()
    {
        var (bugs, db, tenant) = await BuildAsync();
        var (_, _, id) = await bugs.SaveAsync(tenant, Body(shot: Convert.ToBase64String(Jpeg)));

        (await bugs.SetStatusAsync(id, BugReportStatus.Fixed, "Poprawione w 1.2", tellAuthor: false)).Ok.Should().BeTrue();
        var fixedRow = await bugs.GetAsync(id);
        fixedRow!.ClosedAt.Should().NotBeNull();
        fixedRow.AdminNote.Should().Be("Poprawione w 1.2");
        (await bugs.SetStatusAsync(id, BugReportStatus.New, null, false)).Ok.Should().BeTrue();
        (await bugs.GetAsync(id))!.ClosedAt.Should().BeNull();

        // Zamknięte ponad 90 dni temu: przy następnym zgłoszeniu zrzut znika, komentarz zostaje.
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var r = await ctx.BugReports.FindAsync(id);
            r!.Status = BugReportStatus.Closed;
            r.ClosedAt = DateTime.UtcNow.AddDays(-(BugReportService.KeepScreenshotDays + 1));
            await ctx.SaveChangesAsync();
        }
        await bugs.SaveAsync(tenant, Body("Nowe zgłoszenie"));
        var old = await bugs.GetAsync(id);
        old!.HasScreenshot.Should().BeFalse();
        old.Comment.Should().Be("Nie zapisuje się wizyta");

        var counts = await bugs.CountsAsync();
        counts[BugReportStatus.New].Should().Be(1);
        counts[BugReportStatus.Closed].Should().Be(1);
    }

    private static async Task<(BugReportService Bugs, IDbContextFactory<PortalDbContext> Db, Tenant Tenant)> BuildAsync()
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
        services.AddScoped<BugReportService>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var tenant = new Tenant { Slug = "studio", CompanyName = "Studio Siła" };
        await using (var ctx = await db.CreateDbContextAsync()) { ctx.Tenants.Add(tenant); await ctx.SaveChangesAsync(); }
        return (sp.GetRequiredService<BugReportService>(), db, tenant);
    }
}
