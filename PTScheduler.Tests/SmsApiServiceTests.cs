using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class SmsApiServiceTests
{
    [Fact]
    public async Task SendReminderAsync_QuotaExceeded_ReturnsWithoutCallingProvider()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monthKey = DateTime.UtcNow.Year * 100 + DateTime.UtcNow.Month;
        db.SmsSettings.Add(new SmsSettings
        {
            Id = 1,
            IsEnabled = true,
            ApiToken = "fake-token",
            SenderName = "Test",
            QuotaMonthKey = monthKey,
            QuotaSentCount = 5
        });
        await db.SaveChangesAsync();

        var svc = new SmsApiService(factory, NullLogger<SmsApiService>.Instance);

        var result = await svc.SendReminderAsync("600123456", "Test message", maxPerMonth: 5);

        result.Success.Should().BeFalse();
        result.QuotaExceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SendReminderAsync_Disabled_ReturnsErrorWithoutCallingProvider()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SmsSettings.Add(new SmsSettings { Id = 1, IsEnabled = false, ApiToken = "fake-token" });
        await db.SaveChangesAsync();

        var svc = new SmsApiService(factory, NullLogger<SmsApiService>.Instance);

        var result = await svc.SendReminderAsync("600123456", "Test message", maxPerMonth: 50);

        result.Success.Should().BeFalse();
        result.QuotaExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task GetQuotaStatusAsync_RollsOverOnNewMonth()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SmsSettings.Add(new SmsSettings
        {
            Id = 1,
            IsEnabled = true,
            QuotaMonthKey = 202001, // stale month
            QuotaSentCount = 999
        });
        await db.SaveChangesAsync();

        var svc = new SmsApiService(factory, NullLogger<SmsApiService>.Instance);

        var (sent, max) = await svc.GetQuotaStatusAsync(50);

        sent.Should().Be(0);
        max.Should().Be(50);
    }
}
