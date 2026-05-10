using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class NotificationPreferencesServiceTests
{
    [Fact]
    public async Task GetAsync_NoRow_ReturnsDefaults()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new NotificationPreferencesService(factory);

        var dto = await svc.GetAsync("user-1");

        dto.Should().NotBeNull();
        // All defaults are true per DTO definition
        dto.SessionReminders.Should().BeTrue();
        dto.SessionBooked.Should().BeTrue();
        dto.SessionCancelledByTrainer.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_NoExistingRow_CreatesOne()
    {
        var (factory, db) = TestDb.CreateFresh();
        var svc = new NotificationPreferencesService(factory);

        await svc.SaveAsync("user-1", new NotificationPreferencesDto
        {
            SessionReminders = false,
            SessionBooked = false
        });

        var saved = await db.NotificationPreferences.SingleAsync(p => p.UserId == "user-1");
        saved.SessionReminders.Should().BeFalse();
        saved.SessionBooked.Should().BeFalse();
        // Untouched fields stay default
        saved.SessionRescheduled.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_ExistingRow_Updates_DoesNotInsert()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.NotificationPreferences.Add(new NotificationPreferences
        {
            UserId = "user-1",
            SessionReminders = true,
            SessionBooked = true
        });
        await db.SaveChangesAsync();

        var svc = new NotificationPreferencesService(factory);
        await svc.SaveAsync("user-1", new NotificationPreferencesDto { SessionReminders = false });

        // Use a fresh DbContext so we don't see the test fixture's stale change-tracker state.
        await using var freshDb = factory.CreateDbContext();
        var rows = await freshDb.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == "user-1").ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].SessionReminders.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_SessionReminders_RespectsStoredValue()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.NotificationPreferences.Add(new NotificationPreferences
        {
            UserId = "opt-out-user",
            SessionReminders = false
        });
        await db.SaveChangesAsync();

        var svc = new NotificationPreferencesService(factory);

        var disabled = await svc.IsEnabledAsync("opt-out-user", NotificationTypes.SessionReminders);
        disabled.Should().BeFalse();

        // User without a row should be opted in (defaults).
        var defaultEnabled = await svc.IsEnabledAsync("brand-new-user", NotificationTypes.SessionReminders);
        defaultEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_UnknownType_ReturnsTrue()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new NotificationPreferencesService(factory);

        var enabled = await svc.IsEnabledAsync("anyone", "BogusEventNobodyKnowsAbout");
        enabled.Should().BeTrue();
    }
}
