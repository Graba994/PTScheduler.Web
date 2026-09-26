namespace PTScheduler.Domain.Enums;

public enum MembershipStatus
{
    /// <summary>Czeka na pierwszą płatność (zakup w sklepie).</summary>
    Pending = 0,
    Active = 1,
    /// <summary>Zaległa płatność po terminie.</summary>
    PastDue = 2,
    /// <summary>Wstrzymany przez trenera — nie tworzy nowych okresów.</summary>
    Paused = 3,
    Cancelled = 4
}

public enum MembershipPeriodStatus
{
    Due = 0,
    Paid = 1,
    /// <summary>Umorzone przez trenera (np. okres gratis).</summary>
    Waived = 2
}
