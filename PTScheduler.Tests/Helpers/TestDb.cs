using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Tests.Helpers;

/// <summary>
/// Spawns a fresh, isolated EF Core in-memory <see cref="ApplicationDbContext"/> per test.
/// Each call uses a unique DB name (GUID) so tests don't bleed into each other.
/// </summary>
internal static class TestDb
{
    public static (IDbContextFactory<ApplicationDbContext> Factory, ApplicationDbContext Db) CreateFresh()
    {
        var dbName = $"test_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new SimpleFactory(options);
        return (factory, factory.CreateDbContext());
    }

    private sealed class SimpleFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
