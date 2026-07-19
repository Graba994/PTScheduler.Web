using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Order
{
    public int Id { get; set; }

    public string ApplicationUserId { get; set; } = string.Empty;

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    // Our unique order reference sent to PayU as extOrderId.
    public string ExtOrderId { get; set; } = string.Empty;
    // PayU's own order id, filled after the order is created.
    public string? PayUOrderId { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
}
