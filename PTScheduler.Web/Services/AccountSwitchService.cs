using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>Konto, na które można się przełączyć.</summary>
public sealed record SwitchCandidate(string Id, string Name, string Email, string Role);

/// <summary>
/// Przełączanie kont jednym kliknięciem, bez hasła:
/// administrator studia → swoi trenerzy i asystenci; konto techniczne (Root) → każde konto
/// (np. klient, żeby przetestować aplikację jego oczami). Kto się przełączył, zapisujemy
/// w ciasteczku logowania (<see cref="OriginalUserClaim"/>), więc powrót też jest jednym kliknięciem.
/// Wyłączenie: zmienna środowiskowa ACCOUNT_SWITCHING_ENABLED=false.
/// </summary>
public sealed class AccountSwitchService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAuditLogService audit,
    IConfiguration config,
    ILogger<AccountSwitchService> logger)
{
    public const string OriginalUserClaim = "pt:switched_from";

    public bool Enabled => !string.Equals(config["ACCOUNT_SWITCHING_ENABLED"], "false", StringComparison.OrdinalIgnoreCase);

    public static string? OriginalUserId(ClaimsPrincipal user) => user.FindFirst(OriginalUserClaim)?.Value;

    public static bool IsSwitched(ClaimsPrincipal user) => OriginalUserId(user) is not null;

    private static bool CanSwitch(ICollection<string> actorRoles, ICollection<string> targetRoles) =>
        PTScheduler.Domain.Rules.AccountSwitchRules.CanSwitch(actorRoles, targetRoles);

    /// <summary>Osoba, która faktycznie siedzi przy komputerze: ta, z której się przełączono, albo bieżące konto.</summary>
    public async Task<ApplicationUser?> GetActorAsync(ClaimsPrincipal principal)
    {
        var id = OriginalUserId(principal) ?? userManager.GetUserId(principal);
        return id is null ? null : await FindAsync(id);
    }

    // Odczyty na własnym kontekście: pasek w układzie strony i sama strona renderują się
    // równolegle, a UserManager dzieli jeden DbContext na całe żądanie.
    private async Task<ApplicationUser?> FindAsync(string id)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
    }

    private async Task<List<string>> RolesOfAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await (from ur in db.UserRoles
                      join r in db.Roles on ur.RoleId equals r.Id
                      where ur.UserId == userId
                      select r.Name!).ToListAsync();
    }

    /// <summary>Czy przy komputerze siedzi konto techniczne (także po przełączeniu na inne).</summary>
    public async Task<bool> IsRootAsync(ClaimsPrincipal principal)
    {
        var actor = await GetActorAsync(principal);
        return actor is not null && (await RolesOfAsync(actor.Id)).Contains(Roles.Root);
    }

    /// <summary>Właściciel studia przy komputerze (administrator, nie konto techniczne) — także po przełączeniu na swój profil trenera.</summary>
    public async Task<ApplicationUser?> GetOwnerActorAsync(ClaimsPrincipal principal)
    {
        var actor = await GetActorAsync(principal);
        if (actor is null) return null;
        var roles = await RolesOfAsync(actor.Id);
        return roles.Contains(Roles.Admin) && !roles.Contains(Roles.Root) ? actor : null;
    }

    public async Task<bool> CanUseAsync(ClaimsPrincipal principal)
    {
        if (!Enabled) return false;
        var actor = await GetActorAsync(principal);
        return actor is not null && (await RolesOfAsync(actor.Id)).Contains(Roles.Admin);
    }

    /// <summary>Konta, na które ta osoba może się przełączyć (bez bieżącego).</summary>
    public async Task<List<SwitchCandidate>> GetCandidatesAsync(ClaimsPrincipal principal)
    {
        if (!Enabled) return [];
        var actor = await GetActorAsync(principal);
        if (actor is null) return [];
        var actorRoles = await RolesOfAsync(actor.Id);
        var currentId = userManager.GetUserId(principal);

        await using var db = dbFactory.CreateDbContext();
        var rows = await (from u in db.Users.AsNoTracking()
                          join ur in db.UserRoles on u.Id equals ur.UserId
                          join r in db.Roles on ur.RoleId equals r.Id
                          select new { u.Id, u.Email, u.FirstName, u.LastName, Role = r.Name! }).ToListAsync();

        return rows.GroupBy(r => r.Id)
            .Where(g => g.Key != currentId && g.Key != actor.Id)
            .Select(g => (User: g.First(), Roles: g.Select(x => x.Role).ToList()))
            .Where(x => CanSwitch(actorRoles, x.Roles))
            .Select(x => new SwitchCandidate(
                x.User.Id,
                $"{x.User.FirstName} {x.User.LastName}".Trim() is { Length: > 0 } n ? n : x.User.Email ?? "",
                x.User.Email ?? "",
                MainRole(x.Roles)))
            .OrderBy(c => RoleOrder(c.Role)).ThenBy(c => c.Name)
            .ToList();
    }

    /// <summary>Przełącza na wskazane konto. Zwraca komunikat błędu albo null.</summary>
    public async Task<string?> SwitchToAsync(ClaimsPrincipal principal, string targetId)
    {
        if (!Enabled) return "Przełączanie kont jest wyłączone.";
        var actor = await GetActorAsync(principal);
        if (actor is null) return "Zaloguj się ponownie.";
        if (targetId == actor.Id) return await SwitchBackAsync(principal);

        var target = await userManager.FindByIdAsync(targetId);
        if (target is null) return "Nie ma takiego konta.";
        if (!CanSwitch(await RolesOfAsync(actor.Id), await RolesOfAsync(target.Id)))
            return "Nie możesz przełączyć się na to konto.";

        await signInManager.SignInWithClaimsAsync(target, isPersistent: false,
            [new Claim(OriginalUserClaim, actor.Id)]);
        await LogAsync(actor, $"Przełączenie na konto {target.Email}", target.Id);
        return null;
    }

    /// <summary>Wraca na konto, z którego się przełączono.</summary>
    public async Task<string?> SwitchBackAsync(ClaimsPrincipal principal)
    {
        var originalId = OriginalUserId(principal);
        var original = originalId is null ? null : await userManager.FindByIdAsync(originalId);
        if (original is null || !(await RolesOfAsync(original.Id)).Contains(Roles.Admin))
        {
            await signInManager.SignOutAsync();
            return "Zaloguj się ponownie.";
        }
        await signInManager.SignInAsync(original, isPersistent: false);
        await LogAsync(original, "Powrót na własne konto", original.Id);
        return null;
    }

    private async Task LogAsync(ApplicationUser actor, string details, string entityId)
    {
        try
        {
            var role = (await RolesOfAsync(actor.Id)).Contains(Roles.Root) ? Roles.Root : Roles.Admin;
            await audit.LogAsync(actor.Id, actor.Email ?? "", role, "AccountSwitch", "User", entityId, details, AuditSeverity.Warning);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Nie udało się zapisać przełączenia konta w historii zmian."); }
    }

    public static string MainRole(ICollection<string> roles) =>
        roles.Contains(Roles.Root) ? Roles.Root
        : roles.Contains(Roles.Admin) ? Roles.Admin
        : roles.Contains(Roles.Trainer) ? Roles.Trainer
        : roles.Contains(Roles.Subordinate) ? Roles.Subordinate
        : Roles.Client;

    public static string RoleLabel(string role) => role switch
    {
        Roles.Root => "Konto techniczne",
        Roles.Admin => "Administrator studia",
        Roles.Trainer => "Trener",
        Roles.Subordinate => "Asystent trenera",
        _ => "Klient"
    };

    private static int RoleOrder(string role) => role switch
    {
        Roles.Admin => 0, Roles.Trainer => 1, Roles.Subordinate => 2, _ => 3
    };
}
