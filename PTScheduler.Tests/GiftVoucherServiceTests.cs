using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Marketing;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class GiftVoucherServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0);

    // Aplikacja ustawia licencję przy starcie (AddInfrastructure) — w testach robimy to sami.
    static GiftVoucherServiceTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static async Task<IDbContextFactory<ApplicationDbContext>> SeedAsync(bool enabled = true)
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
            db.PackageOffers.Add(new PackageOffer { Id = 1, Name = "Pakiet 5 treningów", SessionTypeId = 1, SessionsCount = 5, Price = 500m, ValidDays = 60, IsActive = true, CreatedByUserId = "t1" });
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "u1", FirstName = "Kasia", LastName = "N", TrainerUserId = "t1" });
            await db.SaveChangesAsync();
        }
        await new MarketingSettingsService(f).SaveAsync(new MarketingSettingsDto { VouchersEnabled = enabled, VoucherAmounts = "100,200", VoucherValidMonths = 12 });
        return f;
    }

    private static GiftVoucherService Make(IDbContextFactory<ApplicationDbContext> f)
    {
        var branding = new Mock<IBrandingService>();
        branding.Setup(b => b.GetAsync()).ReturnsAsync(new AppBrandingDto { CompanyName = "Studio Testowe", ThemeName = "ocean" });
        var web = new Mock<IWebRootPathProvider>();
        web.SetupGet(w => w.WebRootPath).Returns(Path.GetTempPath());
        return new GiftVoucherService(f, new MarketingSettingsService(f), branding.Object, web.Object,
            TestClock.AtWallClock(Now), NullLogger<GiftVoucherService>.Instance);
    }

    [Fact]
    public async Task Online_Purchase_Only_Allows_Configured_Amounts_And_Enabled_Setting()
    {
        var svc = Make(await SeedAsync());
        (await svc.CreatePendingAsync("buyer", new GiftVoucherRequest { Amount = 150 })).Ok.Should().BeFalse();
        (await svc.CreatePendingAsync("buyer", new GiftVoucherRequest { Amount = 200 })).Ok.Should().BeTrue();

        var off = Make(await SeedAsync(enabled: false));
        (await off.CreatePendingAsync("buyer", new GiftVoucherRequest { Amount = 200 })).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Pending_Voucher_Is_Hidden_Until_Paid_Then_Amount_Voucher_Becomes_Single_Use_Coupon()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        var (_, _, id) = await svc.CreatePendingAsync("buyer", new GiftVoucherRequest { Amount = 200, RecipientName = "Ola" });

        (await svc.GetBoughtByAsync("buyer")).Should().BeEmpty("bon czeka na płatność");

        await svc.ActivateAsync(id!.Value);

        var voucher = (await svc.GetBoughtByAsync("buyer")).Single();
        voucher.Code.Should().MatchRegex("^BON-[A-Z2-9]{4}-[A-Z2-9]{4}$");
        voucher.IsUsable.Should().BeTrue();
        voucher.ExpiresAt.Should().Be(voucher.PaidAt!.Value.AddMonths(12));
        await using var db = f.CreateDbContext();
        var coupon = await db.Coupons.SingleAsync();
        (coupon.Code, coupon.DiscountType, coupon.DiscountValue, coupon.MaxUses).Should().Be((voucher.Code, "amount", 200m, 1));
    }

    [Fact]
    public async Task Package_Voucher_Redeems_Once_Into_Paid_Package()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        var (ok, _, voucher) = await svc.IssueAsync("t1", new GiftVoucherRequest { Kind = GiftVoucherKind.Package, PackageOfferId = 1, FromName = "Mama" });
        ok.Should().BeTrue();

        var (redeemed, message) = await svc.RedeemAsync(voucher!.Code.ToLowerInvariant(), clientId: 1);
        redeemed.Should().BeTrue(message);
        (await svc.RedeemAsync(voucher.Code, 1)).Ok.Should().BeFalse("bon można wykorzystać tylko raz");

        await using var db = f.CreateDbContext();
        var pkg = await db.SessionPackages.SingleAsync();
        (pkg.ClientId, pkg.TotalSessions, pkg.IsPaid, pkg.SessionTypeId).Should().Be((1, 5, true, 1));
        pkg.ExpiresAt.Should().NotBeNull();
        (await svc.GetByCodeAsync(voucher.Code))!.Status.Should().Be(GiftVoucherStatus.Redeemed);
    }

    [Fact]
    public async Task Amount_Voucher_Cannot_Be_Redeemed_As_Package_And_Cancel_Disables_Coupon()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        var (_, _, voucher) = await svc.IssueAsync("t1", new GiftVoucherRequest { Amount = 350 });

        var (ok, message) = await svc.RedeemAsync(voucher!.Code, 1);
        ok.Should().BeFalse();
        message.Should().Contain("kupon");

        (await svc.CancelAsync(voucher.Id)).Should().BeTrue();
        await using var db = f.CreateDbContext();
        (await db.Coupons.SingleAsync()).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_Code_Is_Rejected_And_Pdf_Is_Generated()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        (await svc.RedeemAsync("BON-AAAA-BBBB", 1)).Ok.Should().BeFalse();

        var (_, _, voucher) = await svc.IssueAsync("t1", new GiftVoucherRequest { Amount = 100, RecipientName = "Jan", Message = "Wszystkiego dobrego!" });
        var (bytes, fileName) = await svc.GeneratePdfAsync(voucher!.Id);
        fileName.Should().Be($"bon-{voucher.Code}.pdf");
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}

public class PaymentProvidersParsingTests
{
    [Theory]
    [InlineData("""[{"key":"payu","enabled":true,"sandbox":true,"fields":{"posId":"1"}}]""")]
    [InlineData("""{"payu":{"enabled":true,"sandbox":true,"fields":{"posId":"1"}}}""")]
    public void Reads_List_And_Legacy_Dictionary_Format(string json)
    {
        var list = PaymentSettingsService.ParseProviders(json);
        var payu = list.Should().ContainSingle().Subject;
        (payu.Key, payu.Enabled, payu.Get("posId")).Should().Be(("payu", true, "1"));
    }
}
