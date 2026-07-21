namespace PTScheduler.Application.DTOs;

public class OrderDto
{
    public int Id { get; set; }
    /// <summary>Human title of what was bought (course or package name).</summary>
    public string ItemTitle { get; set; } = string.Empty;
    /// <summary>"Course" or "Package".</summary>
    public string Kind { get; set; } = "Course";
    /// <summary>Gateway key that handled the order.</summary>
    public string Provider { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    // Back-compat alias (older markup referenced CourseTitle).
    public string CourseTitle => ItemTitle;
}
