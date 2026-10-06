using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class PermissionServiceTests
{
    [Fact]
    public async Task HasPermission_Admin_AlwaysReturnsTrue()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new PermissionService(factory);

        var result = await svc.HasPermissionAsync(Roles.Admin, Permissions.ManageClients);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermission_Admin_ReturnsTrueEvenForBogusPermission()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new PermissionService(factory);

        var result = await svc.HasPermissionAsync(Roles.Admin, "NonExistentPermission");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermission_Trainer_FallsBackToDefaults()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new PermissionService(factory);

        var manage = await svc.HasPermissionAsync(Roles.Trainer, Permissions.ManageClients);
        manage.Should().BeTrue();

        var audit = await svc.HasPermissionAsync(Roles.Trainer, Permissions.ViewAuditLogs);
        audit.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermission_DbOverride_TakesPrecedenceOverDefaults()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.RolePermissions.Add(new RolePermission
        {
            Role = Roles.Trainer,
            Permission = Permissions.ManageClients,
            IsGranted = false
        });
        await db.SaveChangesAsync();

        var svc = new PermissionService(factory);

        var result = await svc.HasPermissionAsync(Roles.Trainer, Permissions.ManageClients);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermission_DbGrant_OverridesDefaultDenial()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.RolePermissions.Add(new RolePermission
        {
            Role = Roles.Client,
            Permission = Permissions.ManageClients,
            IsGranted = true
        });
        await db.SaveChangesAsync();

        var svc = new PermissionService(factory);

        var result = await svc.HasPermissionAsync(Roles.Client, Permissions.ManageClients);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermission_Client_DefaultsToNoPermissions()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new PermissionService(factory);

        var result = await svc.HasPermissionAsync(Roles.Client, Permissions.ManageClients);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetPermission_CreatesNewRecord()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new PermissionService(factory);

        await svc.SetPermissionAsync(Roles.Trainer, Permissions.ViewAuditLogs, true);

        var result = await svc.HasPermissionAsync(Roles.Trainer, Permissions.ViewAuditLogs);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SetPermission_UpdatesExistingRecord()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.RolePermissions.Add(new RolePermission
        {
            Role = Roles.Trainer, Permission = Permissions.ManageClients, IsGranted = true
        });
        await db.SaveChangesAsync();

        var svc = new PermissionService(factory);
        await svc.SetPermissionAsync(Roles.Trainer, Permissions.ManageClients, false);

        await using var verify = factory.CreateDbContext();
        var count = await verify.RolePermissions
            .CountAsync(r => r.Role == Roles.Trainer && r.Permission == Permissions.ManageClients);
        count.Should().Be(1);

        var result = await svc.HasPermissionAsync(Roles.Trainer, Permissions.ManageClients);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SeedDefaults_PopulatesAllRolesAndPermissions()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new PermissionService(factory);

        await svc.SeedDefaultsAsync();

        await using var verify = factory.CreateDbContext();
        var all = await verify.RolePermissions.ToListAsync();

        var expectedCount = Permissions.Defaults.Count * Permissions.All.Length;
        all.Should().HaveCount(expectedCount);

        var trainerManageClients = all.First(r => r.Role == Roles.Trainer && r.Permission == Permissions.ManageClients);
        trainerManageClients.IsGranted.Should().BeTrue();

        var clientManageClients = all.First(r => r.Role == Roles.Client && r.Permission == Permissions.ManageClients);
        clientManageClients.IsGranted.Should().BeFalse();
    }

    [Fact]
    public async Task SeedDefaults_ClearsExistingAndReseeds()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.RolePermissions.Add(new RolePermission
        {
            Role = Roles.Client, Permission = Permissions.ManageClients, IsGranted = true
        });
        await db.SaveChangesAsync();

        var svc = new PermissionService(factory);
        await svc.SeedDefaultsAsync();

        var result = await svc.HasPermissionAsync(Roles.Client, Permissions.ManageClients);
        result.Should().BeFalse();
    }
}
