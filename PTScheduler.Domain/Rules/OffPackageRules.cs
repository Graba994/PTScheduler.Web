namespace PTScheduler.Domain.Rules;

/// <summary>Jak klient może zarezerwować trening, którego nie ma w pakiecie.</summary>
public enum AtTrainerMode
{
    /// <summary>Trener nie przyjmuje płatności „u siebie” przy rezerwacji online.</summary>
    Disabled,
    /// <summary>Prośba o termin — trener akceptuje albo odrzuca.</summary>
    NeedsApproval,
    /// <summary>Termin potwierdzony od razu, płatność przy treningu.</summary>
    Instant
}

/// <summary>Ustawienia trenera dla rezerwacji bez pakietu (część TrainerConfig).</summary>
public sealed record OffPackagePolicy(bool Online, bool AtTrainer, bool NeedsApproval, int UnpaidLimit);

public static class OffPackageRules
{
    public const string PayOnline = "online";
    public const string PayAtTrainer = "trainer";

    /// <summary>Czas na opłacenie rezerwacji online, zanim termin wróci do puli.</summary>
    public static readonly TimeSpan OnlineHold = TimeSpan.FromMinutes(15);

    /// <summary>
    /// „Zapłacę u trenera”: zaufany klient — od razu; trener wymaga akceptacji — prośba;
    /// inaczej od razu, dopóki klient nie ma już <c>UnpaidLimit</c> nieopłaconych wizyt (0 = bez limitu).
    /// </summary>
    public static AtTrainerMode AtTrainer(OffPackagePolicy policy, bool trustedClient, int unpaidVisits)
    {
        if (!policy.AtTrainer) return AtTrainerMode.Disabled;
        if (trustedClient) return AtTrainerMode.Instant;
        if (policy.NeedsApproval) return AtTrainerMode.NeedsApproval;
        if (policy.UnpaidLimit > 0 && unpaidVisits >= policy.UnpaidLimit) return AtTrainerMode.NeedsApproval;
        return AtTrainerMode.Instant;
    }

    /// <summary>Płatność online za pojedynczy trening: trener pozwala, płatności działają, rodzaj ma cenę.</summary>
    public static bool CanPayOnline(OffPackagePolicy policy, bool paymentsEnabled, decimal? singlePrice) =>
        policy.Online && paymentsEnabled && singlePrice is > 0;
}
