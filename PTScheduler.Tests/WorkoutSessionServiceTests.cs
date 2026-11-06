using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Serwerowy backup trwającego treningu (Faza 6): upsert draftu po kliencie,
/// odczyt i usunięcie — pod sync między urządzeniami.
/// </summary>
public class WorkoutSessionServiceTests
{
    private static WorkoutSessionService Make(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(new DateTime(2026, 6, 1, 12, 0, 0)));

    private static async Task SeedClientAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
    {
        await using var db = f.CreateDbContext();
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "cu", FirstName = "Anna", LastName = "Nowak" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SaveDraft_Upserts_Single_Row_Per_Client()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedClientAsync(f);
        var svc = Make(f);

        await svc.SaveDraftAsync(1, "{\"v\":1}");
        await svc.SaveDraftAsync(1, "{\"v\":2}");

        var open = await svc.GetOpenDraftAsync(1);
        open.Should().NotBeNull();
        open!.DraftJson.Should().Be("{\"v\":2}");

        await using var db = f.CreateDbContext();
        (await db.WorkoutSessions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetOpenDraft_Null_When_None()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedClientAsync(f);
        (await Make(f).GetOpenDraftAsync(1)).Should().BeNull();
    }

    [Fact]
    public async Task Discard_Removes_Draft()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedClientAsync(f);
        var svc = Make(f);

        await svc.SaveDraftAsync(1, "{\"v\":1}");
        await svc.DiscardAsync(1);

        (await svc.GetOpenDraftAsync(1)).Should().BeNull();
        await using var db = f.CreateDbContext();
        (await db.WorkoutSessions.CountAsync()).Should().Be(0);
    }
}
