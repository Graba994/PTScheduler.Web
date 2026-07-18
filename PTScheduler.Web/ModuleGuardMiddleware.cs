using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web;

public class ModuleGuardMiddleware(RequestDelegate next)
{
    private static readonly Dictionary<string, Func<Application.DTOs.SiteSettingsDto, bool>> ModuleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/calendar"] = s => s.SchedulerEnabled,
        ["/clients"] = s => s.SchedulerEnabled,
        ["/trainer/intro-config"] = s => s.SchedulerEnabled,
        ["/my"] = s => s.SchedulerEnabled,
        ["/academy"] = s => s.AcademyEnabled,
        ["/shop"] = s => s.ShopEnabled,
        ["/api/checkout"] = s => s.ShopEnabled,
        ["/api/payu/notify"] = s => s.ShopEnabled,
    };

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;

        Func<Application.DTOs.SiteSettingsDto, bool>? check = null;
        foreach (var (prefix, fn) in ModuleMap)
        {
            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                check = fn;
                break;
            }
        }

        if (check is not null)
        {
            var settings = await ctx.RequestServices
                .GetRequiredService<ISiteSettingsService>()
                .GetAsync();

            if (!check(settings))
            {
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var redirect = ctx.User.Identity?.IsAuthenticated == true ? "/panel" : "/";
                ctx.Response.Redirect(redirect);
                return;
            }
        }

        await next(ctx);
    }
}
