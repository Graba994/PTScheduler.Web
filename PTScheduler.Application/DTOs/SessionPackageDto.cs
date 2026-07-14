using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

public class SessionPackageDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int SessionTypeId { get; set; }
    public string SessionTypeName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int TotalSessions { get; set; }
    public int UsedSessions { get; set; }
    public int RemainingCredits => Math.Max(0, TotalSessions - UsedSessions);
    public decimal PricePerSession { get; set; }
    public decimal TotalPrice => PricePerSession * TotalSessions;
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime PurchasedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public PackageStatus Status { get; set; }

    public bool IsExpiredByDate => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow && Status == PackageStatus.Active;

    public bool IsHidden { get; set; }
}

public class SessionPackageSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SessionTypeId { get; set; }
    public string SessionTypeName { get; set; } = string.Empty;
    public int TotalSessions { get; set; }
    public int UsedSessions { get; set; }
    public int RemainingCredits => Math.Max(0, TotalSessions - UsedSessions);
    public DateTime? ExpiresAt { get; set; }
    public PackageStatus Status { get; set; }
    public bool IsPaid { get; set; }
}

public class ExpiringPackageDto
{
    public int PackageId { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public int RemainingCredits { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int DaysLeft { get; set; }
}

public class CreateSessionPackageDto
{
    public int ClientId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SessionTypeId { get; set; }
    public int TotalSessions { get; set; }
    public decimal PricePerSession { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Notes { get; set; }
}

public class UpdateSessionPackageDto
{
    public string Name { get; set; } = string.Empty;
    public int TotalSessions { get; set; }
    public decimal PricePerSession { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Notes { get; set; }
}
