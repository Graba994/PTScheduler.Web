using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>Polecenie: klient <see cref="ReferrerClientId"/> przyprowadził <see cref="ReferredClientId"/>.</summary>
public class Referral
{
    public int Id { get; set; }

    public int ReferrerClientId { get; set; }
    public Client ReferrerClient { get; set; } = null!;

    /// <summary>Każdy klient może być polecony tylko raz (unikalny indeks).</summary>
    public int ReferredClientId { get; set; }
    public Client ReferredClient { get; set; } = null!;

    public ReferralStatus Status { get; set; } = ReferralStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RewardedAt { get; set; }

    /// <summary>Opis przyznanej nagrody, np. „1 darmowa sesja” albo „Kupon -20%: POLEC-7KQ2M9”.</summary>
    public string? RewardDescription { get; set; }
    public string? RewardCouponCode { get; set; }
    public int? RewardPackageId { get; set; }

    /// <summary>Kupon powitalny dla poleconego znajomego.</summary>
    public string? FriendCouponCode { get; set; }
}
