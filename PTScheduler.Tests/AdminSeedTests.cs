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
    }

    [Fact]
    public async Task Renamed_Admin_Does_Not_Resurrect_Default_Account_On_Restart()
    {
        var (sp, users) = await CreateAsync();
        await using var _ = sp;
        await AddAdminAsync(users, "wlasciciel@studio.pl", "Tajne1234!");

        await DbInitializer.SeedAdminAsync(users);

        (await users.FindByEmailAsync(DbInitializer.DefaultAdminEmail)).Should().BeNull();
        (await users.GetUsersInRoleAsync(Roles.Admin)).Should().ContainSingle();
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

        await DbInitializer.SeedAdminAsync(users);

        (await users.FindByEmailAsync(DbInitializer.DefaultAdminEmail)).Should().BeNull();
        (await users.FindByEmailAsync("wlasciciel@studio.pl")).Should().NotBeNull();
    }
}
