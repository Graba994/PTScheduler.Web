using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Marketing;

public class MarketingSettingsDto
{
    public bool ReferralEnabled { get; set; }
    public ReferralRewardKind ReferrerRewardKind { get; set; } = ReferralRewardKind.FreeSessions;
    public decimal ReferrerRewardValue { get; set; } = 1;
    public int? ReferrerRewardSessionTypeId { get; set; }
    public int FriendDiscountPercent { get; set; }
    public int MaxRewardsPerClient { get; set; }

    public bool ReviewsEnabled { get; set; }
    public string? GoogleReviewUrl { get; set; }
    public int ReviewAskAfterSessions { get; set; } = 5;

    /// <summary>Opis nagrody dla polecającego, np. „1 darmowa sesja”, „kupon -20%”.</summary>
    public string RewardLabel => ReferrerRewardKind switch
    {
        ReferralRewardKind.FreeSessions => ReferrerRewardValue == 1 ? "1 darmowa sesja"
            : $"{ReferrerRewardValue:0} darmowe sesje",
        ReferralRewardKind.DiscountPercent => $"kupon -{ReferrerRewardValue:0}%",
        _ => $"kupon -{ReferrerRewardValue:0.##} zł"
    };
}

public class ReferralDto
{
    public int Id { get; set; }
    public int ReferrerClientId { get; set; }
    public string ReferrerName { get; set; } = "";
    public int ReferredClientId { get; set; }
    public string ReferredName { get; set; } = "";
    public ReferralStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RewardedAt { get; set; }
    public string? RewardDescription { get; set; }
    public string? RewardCouponCode { get; set; }
}

/// <summary>Widok klienta: własny kod, statystyki i nagrody.</summary>
public class MyReferralsDto
{
    public string Code { get; set; } = "";
    public List<ReferralDto> Referrals { get; set; } = [];
    /// <summary>Kupon powitalny, jeśli klient sam trafił z polecenia i jeszcze go nie wykorzystał.</summary>
    public string? WelcomeCouponCode { get; set; }
    public int WelcomeDiscountPercent { get; set; }
}
