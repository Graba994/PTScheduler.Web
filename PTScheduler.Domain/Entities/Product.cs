namespace PTScheduler.Domain.Entities;

public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }

    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";

    // Link to an Academy course — purchase grants enrollment.
    public int? CourseId { get; set; }
    public AcademyCourse? Course { get; set; }

    // Override course's DefaultAccessDays; null = use course default.
    public int? AccessDurationDays { get; set; }

    public bool IsActive { get; set; } = false;
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Order> Orders { get; set; } = [];
}
