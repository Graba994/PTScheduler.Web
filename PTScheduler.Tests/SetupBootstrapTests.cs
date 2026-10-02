using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Dane z kreatora rejestracji w Portalu trafiają do aplikacji bez drugiego /setup.</summary>
public class SetupBootstrapTests
{
    private static async Task<(ServiceProvider Sp, UserManager<ApplicationUser> Users, SetupService Setup, Mock<ISiteContentService> Site)> CreateAsync()
    {
        var services = new ServiceCollection();
        var dbName = $"bootstrap_{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        var sp = services.BuildServiceProvider();
        await DbInitializer.SeedRolesAsync(sp.GetRequiredService<RoleManager<IdentityRole>>());
        var db = await sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
        await DbInitializer.SeedSessionTypesAsync(db);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var site = new Mock<ISiteContentService>();
        return (sp, users, new SetupService(sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(), users, site.Object, NullLogger<SetupService>.Instance), site);
    }

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static SetupBootstrapDto Dto(string? passwordHash, string token = "tok-123") => new()
    {
        CompanyName = "Anna Fit Studio",
        OwnerEmail = "anna@example.com",
        OwnerFirstName = "Anna",
        OwnerLastName = "Nowak",
        PasswordHash = passwordHash,
        SetupMode = "self",
        AppTheme = "rose",
        SiteTemplate = "women",
        Offers = [new() { Name = "Trening personalny", DurationMinutes = 60, Price = 140 }, new() { Name = "Zajęcia w grupie", DurationMinutes = 45, Price = 50, IsGroup = true }],
        Package = new() { Name = "Pakiet 8 treningów", SessionsCount = 8, Price = 1000 },
        WelcomeTokenHash = Sha(token),
        WelcomeTokenExpiresUtc = DateTime.UtcNow.AddMinutes(30)
    };

    [Fact]
    public async Task Bootstrap_CreatesOwnerFromHash_AndAppliesWizardChoices()
    {
        var (sp, users, setup, site) = await CreateAsync();
        var hash = new PasswordHasher<object>().HashPassword(new object(), "Mocne-Haslo-1");

        var result = await setup.BootstrapAsync(Dto(hash));

        result.Completed.Should().BeTrue();
        var owner = await users.FindByEmailAsync("anna@example.com");
        owner.Should().NotBeNull();
        (await users.CheckPasswordAsync(owner!, "Mocne-Haslo-1")).Should().BeTrue("hasło z kreatora działa w aplikacji");
        (await users.IsInRoleAsync(owner!, Roles.Admin)).Should().BeTrue();
        owner!.FirstName.Should().Be("Anna");
        owner.MustChangePassword.Should().BeFalse();

        await using var db = await sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
        var branding = await db.AppBrandings.SingleAsync();
        branding.SetupCompleted.Should().BeTrue();
        branding.CompanyName.Should().Be("Anna Fit Studio");
        branding.ThemeName.Should().Be("rose");
        (await setup.IsSetupCompletedAsync()).Should().BeTrue();

        var types = await db.SessionTypes.OrderBy(t => t.Id).ToListAsync();
        types.Select(t => t.Name).Should().Equal("Trening personalny", "Zajęcia w grupie");
        types[0].SinglePrice.Should().Be(140);
        types[1].IsGroup.Should().BeTrue();
        var package = await db.PackageOffers.SingleAsync();
        package.SessionsCount.Should().Be(8);
        package.SessionTypeId.Should().Be(types[0].Id);
        package.CreatedByUserId.Should().Be(owner.Id);

        site.Verify(s => s.SaveAsync(It.Is<SiteContentDto>(c => c.Template == "women" && c.LayoutVersion == 2)), Times.Once);
    }

    [Fact]
    public async Task WelcomeToken_WorksOnce_AndRejectsWrongToken()
    {
        var (_, users, setup, _) = await CreateAsync();
        await setup.BootstrapAsync(Dto(new PasswordHasher<object>().HashPassword(new object(), "Mocne-Haslo-1"), "dobry-token"));
        var owner = await users.FindByEmailAsync("anna@example.com");

        (await setup.RedeemWelcomeTokenAsync("zly-token")).Should().BeNull();
        (await setup.RedeemWelcomeTokenAsync("dobry-token")).Should().Be(owner!.Id);
        (await setup.RedeemWelcomeTokenAsync("dobry-token")).Should().BeNull("token jest jednorazowy");
    }

    [Fact]
    public async Task WithoutPassword_OnlyPrefillsSetup()
    {
        var (_, users, setup, _) = await CreateAsync();

        var result = await setup.BootstrapAsync(Dto(passwordHash: null));

        result.Completed.Should().BeFalse();
        (await setup.IsSetupCompletedAsync()).Should().BeFalse();
        (await users.FindByEmailAsync("anna@example.com")).Should().BeNull();
        var prefill = await setup.GetPrefillAsync();
        prefill.Should().NotBeNull();
        prefill!.Email.Should().Be("anna@example.com");
        prefill.CompanyName.Should().Be("Anna Fit Studio");

        // /setup z wypełnionymi danymi: konto dostaje imię z rejestracji, nie „Administrator”.
        await setup.CompleteSetupAsync("self", "Anna Fit Studio", "anna@example.com", "Mocne-Haslo-1");
        (await users.FindByEmailAsync("anna@example.com"))!.FirstName.Should().Be("Anna");
    }

    [Fact]
    public async Task Bootstrap_Retry_IsIdempotent()
    {
        var (_, users, setup, _) = await CreateAsync();
        var hash = new PasswordHasher<object>().HashPassword(new object(), "Mocne-Haslo-1");
        await setup.BootstrapAsync(Dto(hash, "t1"));

        var again = await setup.BootstrapAsync(Dto(hash, "t2"));

        again.Completed.Should().BeTrue();
        users.Users.Count(u => u.Email == "anna@example.com").Should().Be(1);
        (await setup.RedeemWelcomeTokenAsync("t2")).Should().NotBeNull("powtórka odświeża token wejścia");
    }
}
