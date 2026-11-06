using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace PTScheduler.Infrastructure.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("PTSCHEDULER_CONN")
            ?? "Host=localhost;Database=ptscheduler_dev;Username=postgres;Password=postgres";

        // Te same opcje Identity co w Program.cs (schemat v3 z tabelą kluczy dostępu — Face ID / odcisk palca).
        // Bez nich migracje powstawały z modelu w wersji 1, a aplikacja w działaniu szukała AspNetUserPasskeys.
        var identity = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connStr)
            .UseApplicationServiceProvider(identity)
            .Options;

        return new ApplicationDbContext(options);
    }
}
