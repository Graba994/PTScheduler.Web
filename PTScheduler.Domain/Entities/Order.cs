using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Order
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerName { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public string? PaymentProvider { get; set; }
    public string? ExternalPaymentId { get; set; }

    // Linked Identity user (created on fulfillment if new).
    public string? ApplicationUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public string? Notes { get; set; }
}
