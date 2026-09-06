namespace PTScheduler.Application.DTOs;

public class PackageOfferDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int SessionTypeId { get; set; }
    public string SessionTypeName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int SessionsCount { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public int? ValidDays { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SavePackageOfferDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int SessionTypeId { get; set; }
    public int SessionsCount { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public int? ValidDays { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
}
