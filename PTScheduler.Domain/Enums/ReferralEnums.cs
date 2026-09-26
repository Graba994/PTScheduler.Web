namespace PTScheduler.Domain.Enums;

/// <summary>Nagroda dla klienta, który polecił studio znajomemu.</summary>
public enum ReferralRewardKind
{
    /// <summary>Darmowe sesje — pakiet opłacony z góry.</summary>
    FreeSessions = 0,
    /// <summary>Jednorazowy kupon procentowy do sklepu.</summary>
    DiscountPercent = 1,
    /// <summary>Jednorazowy kupon kwotowy (zł) do sklepu.</summary>
    DiscountAmount = 2
}

public enum ReferralStatus
{
    /// <summary>Znajomy założył konto — czekamy na jego pierwszą odbytą wizytę.</summary>
    Pending = 0,
    Rewarded = 1,
    /// <summary>Anulowane przez trenera (np. nadużycie) albo po limicie nagród.</summary>
    Cancelled = 2
}
