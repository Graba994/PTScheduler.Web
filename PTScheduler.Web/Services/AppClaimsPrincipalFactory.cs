using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>Dokłada do ciasteczka logowania znacznik „musi zmienić hasło”.</summary>
public class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    public const string MustChangePasswordClaim = "pt:must_change_password";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
            identity.AddClaim(new Claim(MustChangePasswordClaim, "1"));
        return identity;
    }
}
