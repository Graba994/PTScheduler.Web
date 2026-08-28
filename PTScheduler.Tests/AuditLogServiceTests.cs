using FluentAssertions;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class AuditLogServiceTests
{
    [Fact]
    public async Task LogAsync_CreatesRecordWithAllFields()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);

        await svc.LogAsync("user1", "user@test.com", "Admin", "Create", "Client", "42", "Nowy klient", AuditSeverity.Info);

        var logs = await svc.GetLogsAsync();
        logs.Should().HaveCount(1);

        var log = logs[0];
        log.UserId.Should().Be("user1");
        log.UserEmail.Should().Be("user@test.com");
        log.UserRole.Should().Be("Admin");
        log.Action.Should().Be("Create");
        log.EntityType.Should().Be("Client");
        log.EntityId.Should().Be("42");
        log.Details.Should().Be("Nowy klient");
        log.Severity.Should().Be(AuditSeverity.Info);
        log.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetLogs_FilterByEntityType()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);
        await svc.LogAsync("u1", "a@t.com", "Admin", "Create", "Client");
        await svc.LogAsync("u1", "a@t.com", "Admin", "Delete", "Session");
        await svc.LogAsync("u1", "a@t.com", "Admin", "Update", "Client");

        var logs = await svc.GetLogsAsync(entityTypeFilter: "Client");

        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => l.EntityType == "Client");
    }

    [Fact]
    public async Task GetLogs_FilterBySeverity()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);
        await svc.LogAsync("u1", "a@t.com", "Admin", "Login", "User", severity: AuditSeverity.Info);
        await svc.LogAsync("u1", "a@t.com", "Admin", "FailedLogin", "User", severity: AuditSeverity.Warning);
        await svc.LogAsync("u1", "a@t.com", "Admin", "Error", "System", severity: AuditSeverity.Error);

        var warnings = await svc.GetLogsAsync(severityFilter: AuditSeverity.Warning);

        warnings.Should().HaveCount(1);
        warnings[0].Action.Should().Be("FailedLogin");
    }

    [Fact]
    public async Task GetLogs_SearchFilter_MatchesEmailActionAndDetails()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);
        await svc.LogAsync("u1", "anna@test.com", "Admin", "Create", "Client", details: "Klient Jan");
        await svc.LogAsync("u2", "bob@test.com", "Trainer", "Delete", "Session", details: "Sesja usunięta");

        var byEmail = await svc.GetLogsAsync(search: "anna");
        byEmail.Should().HaveCount(1);

        var byDetails = await svc.GetLogsAsync(search: "Jan");
        byDetails.Should().HaveCount(1);

        var byAction = await svc.GetLogsAsync(search: "Delete");
        byAction.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetLogs_CombinedFilters()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);
        await svc.LogAsync("u1", "a@t.com", "Admin", "Create", "Client", severity: AuditSeverity.Info);
        await svc.LogAsync("u1", "a@t.com", "Admin", "Create", "Session", severity: AuditSeverity.Info);
        await svc.LogAsync("u1", "a@t.com", "Admin", "Error", "Client", severity: AuditSeverity.Error);

        var logs = await svc.GetLogsAsync(entityTypeFilter: "Client", severityFilter: AuditSeverity.Info);

        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be("Create");
        logs[0].EntityType.Should().Be("Client");
    }

    [Fact]
    public async Task GetLogs_RespectsCountLimit()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);
        for (int i = 0; i < 10; i++)
            await svc.LogAsync("u1", "a@t.com", "Admin", $"Action{i}", "Client");

        var logs = await svc.GetLogsAsync(count: 3);

        logs.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetLogs_OrderedNewestFirst()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);
        await svc.LogAsync("u1", "a@t.com", "Admin", "First", "Client");
        await Task.Delay(50);
        await svc.LogAsync("u1", "a@t.com", "Admin", "Second", "Client");

        var logs = await svc.GetLogsAsync();

        logs[0].Action.Should().Be("Second");
        logs[1].Action.Should().Be("First");
    }

    [Fact]
    public async Task GetDistinctEntityTypes_ReturnsAlphabeticallySorted()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new AuditLogService(factory);
        await svc.LogAsync("u1", "a@t.com", "Admin", "X", "Session");
        await svc.LogAsync("u1", "a@t.com", "Admin", "X", "Client");
        await svc.LogAsync("u1", "a@t.com", "Admin", "X", "Session");

        var types = await svc.GetDistinctEntityTypesAsync();

        types.Should().BeEquivalentTo(["Client", "Session"]);
        types.Should().BeInAscendingOrder();
    }
}
