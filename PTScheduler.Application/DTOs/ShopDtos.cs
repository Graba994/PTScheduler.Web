using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public int? CourseId { get; set; }
    public string? CourseTitle { get; set; }
    public int? AccessDurationDays { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int OrderCount { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public OrderStatus Status { get; set; }
    public string? PaymentProvider { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? ApplicationUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? Notes { get; set; }
}

public class CheckoutRequest
{
    public int ProductId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
}
