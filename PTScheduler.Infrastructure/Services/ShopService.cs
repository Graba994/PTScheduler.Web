using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ShopService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IAcademyCatalogService academyCatalog) : IShopService
{
    // ---- Products ----

    public async Task<List<ProductDto>> GetProductsAsync(bool activeOnly = false)
    {
        var query = db.Products
            .Include(p => p.Course)
            .Include(p => p.Orders)
            .AsQueryable();

        if (activeOnly)
            query = query.Where(p => p.IsActive);

        var products = await query
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync();

        return products.Select(MapProduct).ToList();
    }

    public async Task<ProductDto?> GetProductAsync(int productId)
    {
        var p = await db.Products
            .Include(p => p.Course)
            .Include(p => p.Orders)
            .FirstOrDefaultAsync(p => p.Id == productId);

        return p is null ? null : MapProduct(p);
    }

    public async Task<int> CreateProductAsync(ProductDto dto)
    {
        var product = new Product
        {
            Name = dto.Name,
            Description = dto.Description,
            CoverImageUrl = dto.CoverImageUrl,
            Price = dto.Price,
            Currency = dto.Currency,
            CourseId = dto.CourseId,
            AccessDurationDays = dto.AccessDurationDays,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    public async Task UpdateProductAsync(ProductDto dto)
    {
        var product = await db.Products.FindAsync(dto.Id)
            ?? throw new InvalidOperationException("Produkt nie istnieje.");
        product.Name = dto.Name;
        product.Description = dto.Description;
        product.CoverImageUrl = dto.CoverImageUrl;
        product.Price = dto.Price;
        product.Currency = dto.Currency;
        product.CourseId = dto.CourseId;
        product.AccessDurationDays = dto.AccessDurationDays;
        product.IsActive = dto.IsActive;
        product.SortOrder = dto.SortOrder;
        await db.SaveChangesAsync();
    }

    public async Task DeleteProductAsync(int productId)
    {
        var product = await db.Products.FindAsync(productId);
        if (product is null) return;
        db.Products.Remove(product);
        await db.SaveChangesAsync();
    }

    // ---- Orders ----

    public async Task<List<OrderDto>> GetOrdersAsync()
    {
        var orders = await db.Orders
            .Include(o => o.Product)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return orders.Select(MapOrder).ToList();
    }

    public async Task<List<OrderDto>> GetOrdersByUserAsync(string applicationUserId)
    {
        var orders = await db.Orders
            .Include(o => o.Product)
            .Where(o => o.ApplicationUserId == applicationUserId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return orders.Select(MapOrder).ToList();
    }

    public async Task<OrderDto?> GetOrderAsync(int orderId)
    {
        var o = await db.Orders
            .Include(o => o.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        return o is null ? null : MapOrder(o);
    }

    public async Task<int> CreateOrderAsync(CheckoutRequest request, string paymentProvider)
    {
        var product = await db.Products.FindAsync(request.ProductId)
            ?? throw new InvalidOperationException("Produkt nie istnieje.");

        var order = new Order
        {
            ProductId = product.Id,
            CustomerEmail = request.Email,
            CustomerName = request.Name,
            Amount = product.Price,
            Currency = product.Currency,
            PaymentProvider = paymentProvider,
            Status = OrderStatus.Pending
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    public async Task MarkOrderPaidAsync(int orderId, string externalPaymentId)
    {
        var order = await db.Orders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Zamówienie nie istnieje.");
        order.Status = OrderStatus.Paid;
        order.ExternalPaymentId = externalPaymentId;
        order.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task UpdateOrderStatusAsync(int orderId, OrderStatus status)
    {
        var order = await db.Orders.FindAsync(orderId)
            ?? throw new InvalidOperationException("Zamówienie nie istnieje.");
        order.Status = status;
        await db.SaveChangesAsync();
    }

    // ---- Fulfillment ----

    public async Task FulfillOrderAsync(int orderId)
    {
        var order = await db.Orders
            .Include(o => o.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException("Zamówienie nie istnieje.");

        if (order.Status != OrderStatus.Paid)
            return;

        // Find or create user account
        var user = await userManager.FindByEmailAsync(order.CustomerEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = order.CustomerEmail,
                Email = order.CustomerEmail,
                EmailConfirmed = true,
                FirstName = ExtractFirstName(order.CustomerName),
                LastName = ExtractLastName(order.CustomerName)
            };
            var password = GenerateTemporaryPassword();
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Nie udało się utworzyć konta: {string.Join(", ", result.Errors.Select(e => e.Description))}");

            await userManager.AddToRoleAsync(user, Roles.Client);
            order.Notes = $"Konto utworzone automatycznie. Hasło tymczasowe: {password}";
        }

        order.ApplicationUserId = user.Id;
        await db.SaveChangesAsync();

        // Grant course enrollment if product is linked to a course
        if (order.Product.CourseId is int courseId)
        {
            DateTime? expiresAt = null;
            if (order.Product.AccessDurationDays is int days and > 0)
                expiresAt = DateTime.UtcNow.AddDays(days);

            await academyCatalog.EnrollAsync(courseId, user.Id, expiresAt);
        }
    }

    private static string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var random = new Random();
        var password = new char[12];
        for (int i = 0; i < password.Length; i++)
            password[i] = chars[random.Next(chars.Length)];
        return new string(password) + "!1";
    }

    private static string? ExtractFirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var parts = fullName.Trim().Split(' ', 2);
        return parts[0];
    }

    private static string? ExtractLastName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var parts = fullName.Trim().Split(' ', 2);
        return parts.Length > 1 ? parts[1] : null;
    }

    private static ProductDto MapProduct(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        CoverImageUrl = p.CoverImageUrl,
        Price = p.Price,
        Currency = p.Currency,
        CourseId = p.CourseId,
        CourseTitle = p.Course?.Title,
        AccessDurationDays = p.AccessDurationDays,
        IsActive = p.IsActive,
        SortOrder = p.SortOrder,
        OrderCount = p.Orders.Count(o => o.Status == OrderStatus.Paid)
    };

    private static OrderDto MapOrder(Order o) => new()
    {
        Id = o.Id,
        ProductId = o.ProductId,
        ProductName = o.Product.Name,
        CustomerEmail = o.CustomerEmail,
        CustomerName = o.CustomerName,
        Amount = o.Amount,
        Currency = o.Currency,
        Status = o.Status,
        PaymentProvider = o.PaymentProvider,
        ExternalPaymentId = o.ExternalPaymentId,
        ApplicationUserId = o.ApplicationUserId,
        CreatedAt = o.CreatedAt,
        PaidAt = o.PaidAt,
        Notes = o.Notes
    };
}
