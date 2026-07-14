using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Web.Components.Account.Pages;
using PTScheduler.Web.Components.Account.Pages.Manage;
using PTScheduler.Infrastructure.Data;
using System.Security.Claims;
using System.Text.Json;

namespace Microsoft.AspNetCore.Routing
{
    internal static class IdentityComponentsEndpointRouteBuilderExtensions
    {
        // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
        public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var accountGroup = endpoints.MapGroup("/Account");

            accountGroup.MapPost("/PerformExternalLogin", (
                HttpContext context,
                [FromServices] SignInManager<ApplicationUser> signInManager,
                [FromForm] string provider,
                [FromForm] string returnUrl) =>
            {
                IEnumerable<KeyValuePair<string, StringValues>> query = [
                    new("ReturnUrl", returnUrl),
                    new("Action", ExternalLogin.LoginCallbackAction)];

                var redirectUrl = UriHelper.BuildRelative(
                    context.Request.PathBase,
                    "/Account/ExternalLogin",
                    QueryString.Create(query));

                var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
                return TypedResults.Challenge(properties, [provider]);
            });

            accountGroup.MapPost("/Logout", async (
                ClaimsPrincipal user,
                [FromServices] SignInManager<ApplicationUser> signInManager,
                [FromForm] string returnUrl) =>
            {
                await signInManager.SignOutAsync();
                var safeReturn = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
                return TypedResults.LocalRedirect($"~/{safeReturn.TrimStart('/')}");
            });

            accountGroup.MapPost("/PasskeyCreationOptions", async (
                HttpContext context,
                [FromServices] UserManager<ApplicationUser> userManager,
                [FromServices] SignInManager<ApplicationUser> signInManager,
                [FromServices] IAntiforgery antiforgery) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                var user = await userManager.GetUserAsync(context.User);
                if (user is null)
                {
                    return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
                }

                var userId = await userManager.GetUserIdAsync(user);
                var userName = await userManager.GetUserNameAsync(user) ?? "User";
                var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(new()
                {
                    Id = userId,
                    Name = userName,
                    DisplayName = userName
                });
                return TypedResults.Content(optionsJson, contentType: "application/json");
            });

            accountGroup.MapPost("/PasskeyRequestOptions", async (
                HttpContext context,
                [FromServices] UserManager<ApplicationUser> userManager,
                [FromServices] SignInManager<ApplicationUser> signInManager,
                [FromServices] IAntiforgery antiforgery,
                [FromQuery] string? username) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                var user = string.IsNullOrEmpty(username) ? null : await userManager.FindByNameAsync(username);
                var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(user);
                return TypedResults.Content(optionsJson, contentType: "application/json");
            });

            var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

            manageGroup.MapPost("/LinkExternalLogin", async (
                HttpContext context,
                [FromServices] SignInManager<ApplicationUser> signInManager,
                [FromForm] string provider) =>
            {
                // Clear the existing external cookie to ensure a clean login process
                await context.SignOutAsync(IdentityConstants.ExternalScheme);

                var redirectUrl = UriHelper.BuildRelative(
                    context.Request.PathBase,
                    "/Account/Manage/ExternalLogins",
                    QueryString.Create("Action", ExternalLogins.LinkLoginCallbackAction));

                var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, signInManager.UserManager.GetUserId(context.User));
                return TypedResults.Challenge(properties, [provider]);
            });

            var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

            manageGroup.MapPost("/DownloadPersonalData", async (
                HttpContext context,
                [FromServices] UserManager<ApplicationUser> userManager,
                [FromServices] AuthenticationStateProvider authenticationStateProvider,
                [FromServices] ApplicationDbContext db) =>
            {
                var user = await userManager.GetUserAsync(context.User);
                if (user is null)
                {
                    return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
                }

                var userId = await userManager.GetUserIdAsync(user);
                downloadLogger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

                var export = new Dictionary<string, object?>
                {
                    ["ExportDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["Account"] = new
                    {
                        user.FirstName,
                        user.LastName,
                        user.Email,
                        user.PhoneNumber,
                        user.TwoFactorEnabled,
                        user.EmailConfirmed
                    }
                };

                var client = await db.Clients
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.ApplicationUserId == userId);

                if (client != null)
                {
                    export["Profile"] = new
                    {
                        client.Phone,
                        client.TrainingGoal,
                        client.DateOfBirth,
                        client.CreatedAt,
                        client.TermsAcceptedAt
                    };

                    var sessions = await db.Sessions
                        .AsNoTracking()
                        .Include(s => s.SessionType)
                        .Where(s => s.ClientId == client.Id)
                        .Select(s => new { s.StartTime, s.Status, SessionType = s.SessionType.Name, s.Notes })
                        .ToListAsync();
                    export["Sessions"] = sessions;

                    var measurements = await db.BodyMeasurements
                        .AsNoTracking()
                        .Where(m => m.ClientId == client.Id)
                        .OrderByDescending(m => m.MeasurementDate)
                        .Select(m => new { m.MeasurementDate, m.WeightKg, m.BodyFatPercent, m.ChestCm, m.WaistCm, m.HipsCm, m.ThighCm, m.ArmCm, m.Notes })
                        .ToListAsync();
                    export["BodyMeasurements"] = measurements;

                    var packages = await db.SessionPackages
                        .AsNoTracking()
                        .Where(p => p.ClientId == client.Id)
                        .Select(p => new { p.Name, p.TotalSessions, p.UsedSessions, p.PricePerSession, p.PurchasedAt, p.ExpiresAt, p.Status })
                        .ToListAsync();
                    export["Packages"] = packages;
                }

                var loginLogs = await db.LoginLogs
                    .AsNoTracking()
                    .Where(l => l.UserId == userId)
                    .OrderByDescending(l => l.LoginTime)
                    .Take(100)
                    .Select(l => new { l.LoginTime, l.IpAddress, l.Success })
                    .ToListAsync();
                export["LoginHistory"] = loginLogs;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var fileBytes = JsonSerializer.SerializeToUtf8Bytes(export, options);

                context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=MojeDane.json");
                return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "MojeDane.json");
            });

            return accountGroup;
        }
    }
}
