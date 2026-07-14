using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class SessionPackage
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string CreatedByUserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public int SessionTypeId { get; set; }
    public SessionType SessionType { get; set; } = null!;

    public int TotalSessions { get; set; }
    public int UsedSessions { get; set; } = 0;

    public decimal PricePerSession { get; set; }

    public bool IsPaid { get; set; } = false;
    public DateTime? PaidAt { get; set; }
    public string? PaymentReference { get; set; }

    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public PackageStatus Status { get; set; } = PackageStatus.Active;

    public bool IsHidden { get; set; }

    public ICollection<Session> Sessions { get; set; } = [];
}
