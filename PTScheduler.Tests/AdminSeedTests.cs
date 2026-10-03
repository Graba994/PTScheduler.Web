using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;
using Xunit;

namespace PTScheduler.Tests;

public class AdminSeedTests
{
    private static async Task<(ServiceProvider Sp, UserManager<ApplicationUser> Users)> CreateAsync()
    {
        var services = new ServiceCollection();
        var dbName = $"admin_{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        var sp = services.BuildServiceProvider();
        await DbInitializer.SeedRolesAsync(sp.GetRequiredService<RoleManager<IdentityRole>>());
        return (sp, sp.GetRequiredService<UserManager<ApplicationUser>>());
    }

    private static async Task<ApplicationUser> AddAdminAsync(UserManager<ApplicationUser> users, string email, string password)
    {
        var u = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(u, password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(u, Roles.Admin);
        return u;
    }

    [Fact]
    public async Task Fresh_Database_Gets_Default_Admin_Without_Known_Password()
    {
        var (sp, users) = await CreateAsync();
        await using var _ = sp;

        await DbInitializer.SeedAdminAsync(users);

        var root = await users.FindByEmailAsync(DbInitializer.DefaultAdminEmail);
        root.Should().NotBeNull();
        (await users.IsInRoleAsync(root!, Roles.Admin)).Should().BeTrue();
        (await users.CheckPasswordAsync(root!, "password")).Should().BeFalse();
        root!.MustChangePassword.Should().BeTrue("hasło startowe jest losowe — trzeba ustawić własne");
    }

    [Fact]
    public async Task Fresh_Default_Admin_Is_Technical_Root()
    {
        var (sp, users) = await CreateAsync();
        await using var _ = sp;

        await DbInitializer.SeedAdminAsync(users);

        var root = await users.FindByEmailAsync(DbInitializer.DefaultAdminEmail);
        (await users.IsInRoleAsync(root!, Roles.Root)).Should().BeTrue();
        (await DbInitializer.FindOwnerAdminAsync(users)).Should().BeNull("przed kreatorem nie ma jeszcze administratora studia");
    }

    [Fact]
    public async Task Renamed_Admin_Gets_Separate_Technical_Root_With_Random_Password()
    {
        var (sp, users) = await CreateAsync();
        await using var _ = sp;
        var owner = await AddAdminAsync(users, "wlasciciel@studio.pl", "Tajne1234!");

        await DbInitializer.SeedAdminAsync(users);
        await DbInitializer.SeedAdminAsync(users); // restart nie zakłada kolejnego konta

        var root = await users.FindByEmailAsync(DbInitializer.DefaultAdminEmail);
        root.Should().NotBeNull();
        (await users.IsInRoleAsync(root!, Roles.Root)).Should().BeTrue();
        (await users.CheckPasswordAsync(root!, "password")).Should().BeFalse();
        (await users.GetUsersInRoleAsync(Roles.Root)).Should().ContainSingle();
        (await users.IsInRoleAsync(owner, Roles.Root)).Should().BeFalse("właściciel ma mniej funkcji niż konto techniczne");
        (await DbInitializer.FindOwnerAdminAsync(users))!.Id.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Existing_Root_With_Legacy_Password_Must_Change_It()
    {
        var (sp, users) = await CreateAsync();
        await using var _ = sp;
        var root = new ApplicationUser { UserName = DbInitializer.DefaultAdminEmail, Email = DbInitializer.DefaultAdminEmail };
        await users.CreateAsync(root);
        root.PasswordHash = users.PasswordHasher.HashPassword(root, "password");
        await users.UpdateAsync(root);
        await users.AddToRoleAsync(root, Roles.Admin);

        await DbInitializer.SeedAdminAsync(users);

        root = (await users.FindByIdAsync(root.Id))!;
        (await users.IsInRoleAsync(root, Roles.Root)).Should().BeTrue();
        root.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Resurrected_Default_Admin_With_Legacy_Password_Is_Removed()
    {
        var (sp, users) = await CreateAsync();
        await using var _ = sp;
        await AddAdminAsync(users, "wlasciciel@studio.pl", "Tajne1234!");
        var root = new ApplicationUser { UserName = DbInitializer.DefaultAdminEmail, Email = DbInitializer.DefaultAdminEmail };
        await users.CreateAsync(root);
        root.PasswordHash = users.PasswordHasher.HashPassword(root, "password");
        await users.UpdateAsync(root);
        await users.AddToRoleAsync(root, Roles.Admin);

        var legacyId = root.Id;

        await DbInitializer.SeedAdminAsync(users);

        // „Wskrzeszone” konto ze znanym hasłem znika; konto techniczne powstaje od nowa z losowym hasłem.
        (await users.FindByIdAsync(legacyId)).Should().BeNull();
        var fresh = await users.FindByEmailAsync(DbInitializer.DefaultAdminEmail);
        (await users.CheckPasswordAsync(fresh!, "password")).Should().BeFalse();
        (await users.FindByEmailAsync("wlasciciel@studio.pl")).Should().NotBeNull();
    }
}
