using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class PaymentServiceAuditTests
{
    [Fact]
    public async Task CompleteSimulatorAsync_Paid_WritesOrderPaidAuditLog()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Orders.Add(new Order
        {
            ApplicationUserId = "buyer-1",
            Kind = OrderKind.Course,
            Provider = PaymentProviders.Simulator,
            ExtOrderId = "ext-123",
            Amount = 99.99m,
            Currency = "PLN",
            Status = OrderStatus.Pending
        });
        await db.SaveChangesAsync();

        var auditLog = new Mock<IAuditLogService>();
        var svc = new PaymentService(
            factory,
            new Mock<IPaymentSettingsService>().Object,
            new Mock<ICouponService>().Object,
            [],
            auditLog.Object,
            NullLogger<PaymentService>.Instance);

        var ok = await svc.CompleteSimulatorAsync("ext-123", paid: true);

        ok.Should().BeTrue();
        var order = await svc.GetOrderByExtAsync("ext-123");
        order!.Status.Should().Be("Paid");

        auditLog.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            "OrderPaid", "Order", "1", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CompleteSimulatorAsync_Canceled_DoesNotWriteAuditLog()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Orders.Add(new Order
        {
            ApplicationUserId = "buyer-1",
            Kind = OrderKind.Course,
            Provider = PaymentProviders.Simulator,
            ExtOrderId = "ext-456",
            Amount = 50m,
            Currency = "PLN",
            Status = OrderStatus.Pending
        });
        await db.SaveChangesAsync();

        var auditLog = new Mock<IAuditLogService>();
        var svc = new PaymentService(
            factory,
            new Mock<IPaymentSettingsService>().Object,
            new Mock<ICouponService>().Object,
            [],
            auditLog.Object,
            NullLogger<PaymentService>.Instance);

        await svc.CompleteSimulatorAsync("ext-456", paid: false);

        var order = await svc.GetOrderByExtAsync("ext-456");
        order!.Status.Should().Be("Canceled");
        auditLog.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
