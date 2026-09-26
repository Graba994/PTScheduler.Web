using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SetupService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager) : ISetupService
{
    public async Task<bool> IsSetupCompletedAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        return b?.SetupCompleted ?? false;
    }

    public async Task CompleteSetupAsync(string mode, string companyName, string adminEmail, string adminPassword)
    {
        await using var db = dbFactory.CreateDbContext();

        var branding = await db.AppBrandings.FirstOrDefaultAsync();
        if (branding is null)
        {
            branding = new AppBranding();
            db.AppBrandings.Add(branding);
        }

        branding.CompanyName = companyName;
        branding.SetupCompleted = true;
        branding.SetupMode = mode;
        branding.SetupCompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Konto startowe ma losowe hasło (DbInitializer.SeedAdminAsync) — kreator nadaje
        // właścicielowi własny login i hasło. Gdy operator zmienił już adres przez Portal,
        // bierzemy jedynego istniejącego admina.
        var defaultAdmin = await userManager.FindByEmailAsync(DbInitializer.DefaultAdminEmail);
        if (defaultAdmin is null)
        {
            var admins = await userManager.GetUsersInRoleAsync(Domain.Constants.Roles.Admin);
            if (admins.Count == 1) defaultAdmin = admins[0];
        }
        if (defaultAdmin is not null)
        {
            if (!string.Equals(adminEmail, defaultAdmin.Email, StringComparison.OrdinalIgnoreCase))
            {
                defaultAdmin.Email = adminEmail;
                defaultAdmin.NormalizedEmail = adminEmail.ToUpperInvariant();
                defaultAdmin.UserName = adminEmail;
                defaultAdmin.NormalizedUserName = adminEmail.ToUpperInvariant();
            }

            var token = await userManager.GeneratePasswordResetTokenAsync(defaultAdmin);
            await userManager.ResetPasswordAsync(defaultAdmin, token, adminPassword);
        }
    }
}
