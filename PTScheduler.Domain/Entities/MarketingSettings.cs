using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>Jednowierszowa konfiguracja programu poleceń i próśb o opinię.</summary>
public class MarketingSettings
{
    public int Id { get; set; } = 1;

    // ── Program poleceń ─────────────────────────────────
    public bool ReferralEnabled { get; set; }
    public ReferralRewardKind ReferrerRewardKind { get; set; } = ReferralRewardKind.FreeSessions;
    /// <summary>Liczba sesji, procent albo kwota — zależnie od <see cref="ReferrerRewardKind"/>.</summary>
    public decimal ReferrerRewardValue { get; set; } = 1;
    /// <summary>Typ sesji w darmowym pakiecie (nagroda „darmowe sesje”).</summary>
    public int? ReferrerRewardSessionTypeId { get; set; }
    /// <summary>Rabat % dla poleconego znajomego na pierwszy zakup (0 = bez rabatu).</summary>
    public int FriendDiscountPercent { get; set; }
    /// <summary>Maks. liczba nagród dla jednego klienta (0 = bez limitu).</summary>
    public int MaxRewardsPerClient { get; set; }

    // ── Opinie ─────────────────────────────────
    public bool ReviewsEnabled { get; set; }
    /// <summary>Link „Napisz opinię” z Profilu Firmy w Google (g.page/r/…/review).</summary>
    public string? GoogleReviewUrl { get; set; }
    /// <summary>Po ilu odbytych wizytach poprosić klienta o opinię.</summary>
    public int ReviewAskAfterSessions { get; set; } = 5;

    // ── Bony podarunkowe ─────────────────────────────────
    public bool VouchersEnabled { get; set; }
    /// <summary>Kwoty bonów do wyboru w sklepie, np. „100,200,300,500”.</summary>
    public string VoucherAmounts { get; set; } = "100,200,300,500";
    /// <summary>Ważność bonu w miesiącach od zakupu.</summary>
    public int VoucherValidMonths { get; set; } = 12;

    // ── Ocena aplikacji przez trenera (trafia do właściciela platformy) ──
    public DateTime? LastAppFeedbackAt { get; set; }
}
