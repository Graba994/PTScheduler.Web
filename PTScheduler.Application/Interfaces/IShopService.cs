using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Interfaces;

public interface IShopService
{
    // Products (Owner)
    Task<List<ProductDto>> GetProductsAsync(bool activeOnly = false);
    Task<ProductDto?> GetProductAsync(int productId);
    Task<int> CreateProductAsync(ProductDto dto);
    Task UpdateProductAsync(ProductDto dto);
    Task DeleteProductAsync(int productId);

    // Orders
    Task<List<OrderDto>> GetOrdersAsync();
    Task<List<OrderDto>> GetOrdersByUserAsync(string applicationUserId);
    Task<OrderDto?> GetOrderAsync(int orderId);

    // Checkout flow
    Task<int> CreateOrderAsync(CheckoutRequest request, string paymentProvider);
    Task MarkOrderPaidAsync(int orderId, string externalPaymentId);
    Task UpdateOrderStatusAsync(int orderId, OrderStatus status);

    // Fulfillment: create user account (if needed) + enrollment
    Task FulfillOrderAsync(int orderId);
}
