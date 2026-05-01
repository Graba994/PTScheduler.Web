using Microsoft.Extensions.DependencyInjection;

namespace PTScheduler.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
