using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
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
        // Właściciel dostaje WŁASNE konto administratora studia. Konto techniczne root@admin.local
        // (rola Root) zostaje osobno — dla operatora platformy (Portal, testy, funkcje techniczne).
        var owner = await userManager.FindByEmailAsync(adminEmail);
        if (owner is not null && await userManager.IsInRoleAsync(owner, Roles.Root))
            throw new InvalidOperationException("Ten adres należy do konta technicznego — podaj własny adres e-mail.");

        if (owner is null)
        {
            owner = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FirstName = "Administrator",
                LastName = companyName
            };
            var created = await userManager.CreateAsync(owner, adminPassword);
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }
        else
        {
            await userManager.RemovePasswordAsync(owner);
            var changed = await userManager.AddPasswordAsync(owner, adminPassword);
            if (!changed.Succeeded)
                throw new InvalidOperationException(string.Join(" ", changed.Errors.Select(e => e.Description)));
        }

        if (!await userManager.IsInRoleAsync(owner, Roles.Admin))
            await userManager.AddToRoleAsync(owner, Roles.Admin);
        // Hasło ustawia sam trener w kreatorze — nie wymuszamy kolejnej zmiany.
        owner.MustChangePassword = false;
        await userManager.UpdateAsync(owner);

        // Kreator jest skończony dopiero, gdy konto właściciela istnieje.
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
    }
}
