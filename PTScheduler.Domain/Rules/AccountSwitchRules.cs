using PTScheduler.Domain.Constants;

namespace PTScheduler.Domain.Rules;

/// <summary>Kto może przełączyć się (bez hasła) na czyje konto.</summary>
public static class AccountSwitchRules
{
    /// <summary>
    /// Konto techniczne (Root) → każde konto poza innym kontem technicznym (np. klient — do testów).
    /// Administrator studia → trenerzy i asystenci. Pozostali — nikt.
    /// </summary>
    public static bool CanSwitch(ICollection<string> actorRoles, ICollection<string> targetRoles)
    {
        if (actorRoles.Contains(Roles.Root))
            return !targetRoles.Contains(Roles.Root);
        if (actorRoles.Contains(Roles.Admin))
            return !targetRoles.Contains(Roles.Admin)
                && (targetRoles.Contains(Roles.Trainer) || targetRoles.Contains(Roles.Subordinate));
        return false;
    }
}
