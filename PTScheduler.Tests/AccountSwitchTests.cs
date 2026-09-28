using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

public class AccountSwitchTests
{
    [Theory]
    // Konto techniczne: na każde konto poza innym kontem technicznym.
    [InlineData(new[] { Roles.Admin, Roles.Root }, new[] { Roles.Admin }, true)]
    [InlineData(new[] { Roles.Admin, Roles.Root }, new[] { Roles.Trainer }, true)]
    [InlineData(new[] { Roles.Admin, Roles.Root }, new[] { Roles.Client }, true)]
    [InlineData(new[] { Roles.Admin, Roles.Root }, new[] { Roles.Admin, Roles.Root }, false)]
    // Administrator studia: tylko trenerzy i asystenci.
    [InlineData(new[] { Roles.Admin }, new[] { Roles.Trainer }, true)]
    [InlineData(new[] { Roles.Admin }, new[] { Roles.Subordinate }, true)]
    [InlineData(new[] { Roles.Admin }, new[] { Roles.Client }, false)]
    [InlineData(new[] { Roles.Admin }, new[] { Roles.Admin }, false)]
    [InlineData(new[] { Roles.Admin }, new[] { Roles.Admin, Roles.Root }, false)]
    // Trener i klient nie przełączają się na nikogo.
    [InlineData(new[] { Roles.Trainer }, new[] { Roles.Client }, false)]
    [InlineData(new[] { Roles.Trainer }, new[] { Roles.Trainer }, false)]
    [InlineData(new[] { Roles.Client }, new[] { Roles.Client }, false)]
    public void Switch_Rules(string[] actor, string[] target, bool allowed) =>
        AccountSwitchRules.CanSwitch(actor, target).Should().Be(allowed);

    private static async Task<(ServiceProvider Sp, UserManager<ApplicationUser> Users, SetupService Setup)> CreateAsync()
    {
        var services = new ServiceCollection();
        var dbName = $"setup_{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        var sp = services.BuildServiceProvider();
        await DbInitializer.SeedRolesAsync(sp.GetRequiredService<RoleManager<IdentityRole>>());
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        await DbInitializer.SeedAdminAsync(users);
        return (sp, users, new SetupService(sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(), users));
    }

    [Fact]
    public async Task Setup_Creates_Separate_Owner_And_Keeps_Technical_Root()
    {
        var (sp, users, setup) = await CreateAsync();
        await using var _ = sp;

        await setup.CompleteSetupAsync("self", "Studio Fit", "anna@studio.pl", "Haslo1234!");

        var owner = await users.FindByEmailAsync("anna@studio.pl");
        owner.Should().NotBeNull();
        (await users.IsInRoleAsync(owner!, Roles.Admin)).Should().BeTrue();
        (await users.IsInRoleAsync(owner!, Roles.Root)).Should().BeFalse();
        (await users.CheckPasswordAsync(owner!, "Haslo1234!")).Should().BeTrue();

        var root = await users.FindByEmailAsync(DbInitializer.DefaultAdminEmail);
        (await users.IsInRoleAsync(root!, Roles.Root)).Should().BeTrue("konto techniczne zostaje osobno");
        (await setup.IsSetupCompletedAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Setup_Refuses_Technical_Account_Email()
    {
        var (sp, _, setup) = await CreateAsync();
        await using var __ = sp;

        var act = () => setup.CompleteSetupAsync("self", "Studio Fit", DbInitializer.DefaultAdminEmail, "Haslo1234!");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await setup.IsSetupCompletedAsync()).Should().BeFalse("bez konta właściciela kreator nie jest skończony");
    }
}
