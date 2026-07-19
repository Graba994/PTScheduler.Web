namespace PTScheduler.Application.DTOs;

public class OrderDto
{
    public int Id { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
