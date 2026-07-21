namespace PTScheduler.Domain.Entities;

/// <summary>
/// A sellable session package template. When a client buys it, a concrete
/// <see cref="SessionPackage"/> (bookable credits) is created for them.
/// </summary>
public class PackageOffer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Which kind of session the credits apply to.</summary>
    public int SessionTypeId { get; set; }
    public SessionType SessionType { get; set; } = null!;

    /// <summary>Number of bookable sessions granted on purchase.</summary>
    public int SessionsCount { get; set; }

    /// <summary>Total price for the whole package.</summary>
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";

    /// <summary>Optional validity in days from purchase (null = no expiry).</summary>
    public int? ValidDays { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
