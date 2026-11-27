using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Studio;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Wszystkie usługi Studia reklam muszą dać się zbudować razem — tak jak w Portalu (StudioRegistration.AddStudioReklam).
/// Pilnuje m.in. zależności cyklicznych: kolejka → wykonawca → usługa filmów → kolejka (wywróciło stronę Studia).
/// </summary>
public class StudioRegistrationTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<EmailService>();
        services.AddScoped<AiService>();
        // Usługi spoza Studia, których Studio używa, ale tu nie są potrzebne (Instagram, instancje trenerów, demo):
        // puste — test sprawdza budowę zależności Studia, nie te moduły.
        services.AddScoped(_ => (InstagramService)null!);
        services.AddScoped(_ => (TenantService)null!);
        services.AddScoped(_ => (TenantDemoService)null!);
        services.AddScoped(_ => (PublicDemoService)null!);
        services.AddStudioReklam(backgroundServices: false);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void EveryStudioService_ResolvesTogether_WithoutCycles()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        var p = scope.ServiceProvider;
        var act = () =>
        {
            p.GetRequiredService<StudioService>();
            p.GetRequiredService<StudioMedia>();
            p.GetRequiredService<RendererClient>();
            p.GetRequiredService<StudioJobs>();
            p.GetRequiredService<StudioScreens>();
            p.GetRequiredService<AdWriter>();
            p.GetRequiredService<StudioVideos>();
            return p.GetServices<IStudioJobHandler>().ToList();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void EveryJobType_HasExactlyOneHandler()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();
        var types = scope.ServiceProvider.GetServices<IStudioJobHandler>().Select(h => h.Type).ToList();
        types.Should().OnlyHaveUniqueItems();
        types.Should().Contain(["sounds", "screens", "write", "voice", "render", "qa"]);
        types.Should().OnlyContain(t => StudioJobs.JobLabel(t) != t, "każdy rodzaj zadania ma polską nazwę w powiadomieniach");
    }
}
