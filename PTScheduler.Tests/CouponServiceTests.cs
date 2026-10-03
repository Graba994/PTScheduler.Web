using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class CouponServiceTests
{
    [Fact]
    public async Task Validate_EmptyCode_ReturnsInvalid()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new CouponService(factory);

        var result = await svc.ValidateAsync("", 100m, "packages");

        result.IsValid.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Validate_NonexistentCode_ReturnsInvalid()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new CouponService(factory);

        var result = await svc.ValidateAsync("FAKE123", 100m, "packages");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_InactiveCode_ReturnsInvalid()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon { Code = "OFF10", IsActive = false, DiscountType = "percent", DiscountValue = 10, Scope = "all" });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("OFF10", 100m, "packages");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_BeforeValidFrom_ReturnsInvalid()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "FUTURE", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "all", ValidFrom = DateTime.UtcNow.AddDays(5)
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("FUTURE", 100m, "packages");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_AfterValidUntil_ReturnsInvalid()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "EXPIRED", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "all", ValidUntil = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("EXPIRED", 100m, "packages");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_MaxUsesReached_ReturnsInvalid()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "LIMIT", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "all", MaxUses = 3, UsedCount = 3
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("LIMIT", 100m, "packages");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_UnlimitedUses_ZeroMaxUses_IsValid()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "UNLIMITED", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "all", MaxUses = 0, UsedCount = 999
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("UNLIMITED", 100m, "packages");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WrongScope_ReturnsInvalid()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "COURSESONLY", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "courses"
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("COURSESONLY", 100m, "packages");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ScopeAll_MatchesAnyTarget()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "ALLSCOPE", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "all"
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);

        (await svc.ValidateAsync("ALLSCOPE", 100m, "packages")).IsValid.Should().BeTrue();
        (await svc.ValidateAsync("ALLSCOPE", 100m, "courses")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_PercentDiscount_CalculatesCorrectly()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "PCT20", IsActive = true, DiscountType = "percent", DiscountValue = 20,
            Scope = "all"
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("PCT20", 150m, "packages");

        result.IsValid.Should().BeTrue();
        result.DiscountAmount.Should().Be(30m);
        result.FinalAmount.Should().Be(120m);
    }

    [Fact]
    public async Task Validate_FixedDiscount_CappedAtOriginalAmount()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "BIG", IsActive = true, DiscountType = "amount", DiscountValue = 200,
            Scope = "all"
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("BIG", 50m, "packages");

        result.IsValid.Should().BeTrue();
        result.DiscountAmount.Should().Be(50m);
        result.FinalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Validate_CaseInsensitiveCodeLookup()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "MYCODE", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "all"
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        var result = await svc.ValidateAsync("mycode", 100m, "packages");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Redeem_IncrementsUsedCount()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.Coupons.Add(new Coupon
        {
            Code = "REDEEM", IsActive = true, DiscountType = "percent", DiscountValue = 10,
            Scope = "all", UsedCount = 0
        });
        await db.SaveChangesAsync();

        var svc = new CouponService(factory);
        await svc.RedeemAsync(1, "user1", "user@test.com", 100m, 10m, 90m, "packages", null);

        await using var verify = factory.CreateDbContext();
        var coupon = await verify.Coupons.FindAsync(1);
        coupon!.UsedCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_NormalizesCodeToUpperInvariant()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new CouponService(factory);

        var id = await svc.CreateAsync(new SaveCouponDto
        {
            Code = "  lowercase  ",
            DiscountType = "percent",
            DiscountValue = 10,
            Scope = "all",
            IsActive = true
        });

        var coupon = await svc.GetAsync(id);
        coupon!.Code.Should().Be("LOWERCASE");
    }

    [Fact]
    public async Task Create_NegativeMaxUses_ClampedToZero()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new CouponService(factory);

        var id = await svc.CreateAsync(new SaveCouponDto
        {
            Code = "TEST",
            DiscountType = "percent",
            DiscountValue = 10,
            MaxUses = -5,
            Scope = "all",
            IsActive = true
        });

        await using var verify = factory.CreateDbContext();
        var coupon = await verify.Coupons.FindAsync(id);
        coupon!.MaxUses.Should().Be(0);
    }
}
