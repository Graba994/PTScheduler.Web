using System.Xml.Linq;
using FluentAssertions;
using PTScheduler.Application.Ksef;
using Xunit;

namespace PTScheduler.Tests;

public class KsefTests
{
    private const string SellerNip = "5260250274"; // poprawna suma kontrolna

    [Theory]
    [InlineData("5260250274", true)]
    [InlineData("PL 526-025-02-74", true)]
    [InlineData("5260250275", false)]
    [InlineData("123", false)]
    public void Nip_Validation(string nip, bool valid) => Nip.IsValid(nip).Should().Be(valid);

    private static Fa3Invoice Invoice(bool vatPayer, string? buyerNip) => new(
        Number: "FV/0001/2026/10",
        IssueDate: new DateOnly(2026, 10, 1),
        SaleDate: new DateOnly(2026, 10, 1),
        Seller: new Fa3Party(SellerNip, "Jan Kowalski Fitness", "ul. Sportowa 1", "00-001 Warszawa"),
        Buyer: new Fa3Party(buyerNip, buyerNip is null ? "Anna Nowak" : "ACME sp. z o.o.", "ul. Biurowa 2", "00-002 Warszawa"),
        Lines: [new Fa3Line("Pakiet 10 treningów", 1, 1230m)],
        VatPayer: vatPayer, VatRate: 23m, VatExemptBasis: null,
        PaidDate: new DateOnly(2026, 10, 1));

    [Fact]
    public void Builds_Vat_Invoice_With_Gross_Lines_And_Totals()
    {
        var xml = Fa3XmlBuilder.Build(Invoice(true, "5260250274"), new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        XNamespace ns = Fa3XmlBuilder.Namespace;
        var doc = XDocument.Parse(xml);
        var fa = doc.Root!.Element(ns + "Fa")!;

        doc.Root.Element(ns + "Naglowek")!.Element(ns + "KodFormularza")!.Attribute("kodSystemowy")!.Value.Should().Be("FA (3)");
        doc.Root.Element(ns + "Podmiot2")!.Element(ns + "DaneIdentyfikacyjne")!.Element(ns + "NIP")!.Value.Should().Be("5260250274");
        fa.Element(ns + "P_13_1")!.Value.Should().Be("1000.00");
        fa.Element(ns + "P_14_1")!.Value.Should().Be("230.00");
        fa.Element(ns + "P_15")!.Value.Should().Be("1230.00");
        fa.Element(ns + "FaWiersz")!.Element(ns + "P_12")!.Value.Should().Be("23");
        fa.Element(ns + "Adnotacje")!.Element(ns + "Zwolnienie")!.Element(ns + "P_19N").Should().NotBeNull();
    }

    [Fact]
    public void Builds_Exempt_Invoice_For_Consumer()
    {
        var xml = Fa3XmlBuilder.Build(Invoice(false, null), DateTime.UtcNow);
        XNamespace ns = Fa3XmlBuilder.Namespace;
        var doc = XDocument.Parse(xml);
        var fa = doc.Root!.Element(ns + "Fa")!;

        doc.Root.Element(ns + "Podmiot2")!.Element(ns + "DaneIdentyfikacyjne")!.Element(ns + "BrakID").Should().NotBeNull();
        fa.Element(ns + "P_13_7")!.Value.Should().Be("1230.00");
        fa.Element(ns + "FaWiersz")!.Element(ns + "P_12")!.Value.Should().Be("zw");
        fa.Element(ns + "Adnotacje")!.Element(ns + "Zwolnienie")!.Element(ns + "P_19A")!.Value.Should().Contain("113");
    }

    [Fact]
    public void Rejects_Invalid_Seller_Nip()
    {
        var inv = Invoice(true, null) with { Seller = new Fa3Party("123", "X", null, null) };
        var act = () => Fa3XmlBuilder.Build(inv, DateTime.UtcNow);
        act.Should().Throw<ArgumentException>();
    }
}

public class KsefServiceTests
{
    private static (PTScheduler.Infrastructure.Services.Ksef.KsefService Svc, Microsoft.EntityFrameworkCore.IDbContextFactory<PTScheduler.Infrastructure.Data.ApplicationDbContext> F) Make()
    {
        var (f, _) = PTScheduler.Tests.Helpers.TestDb.CreateFresh();
        var svc = new PTScheduler.Infrastructure.Services.Ksef.KsefService(
            f,
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
            new Moq.Mock<PTScheduler.Application.Interfaces.IInvoiceService>().Object,
            new Moq.Mock<PTScheduler.Application.Interfaces.IBrandingService>().Object,
            PTScheduler.Tests.Helpers.TestClock.AtWallClock(new DateTime(2026, 10, 1, 12, 0, 0)),
            new PTScheduler.Infrastructure.Services.Ksef.KsefClient(new HttpClient()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PTScheduler.Infrastructure.Services.Ksef.KsefService>.Instance);
        return (svc, f);
    }

    [Fact]
    public async Task SetBuyer_Rejects_Invalid_Nip_And_Saves_Valid()
    {
        var (svc, f) = Make();
        await using (var db = f.CreateDbContext())
        {
            db.Orders.Add(new PTScheduler.Domain.Entities.Order { Id = 1, ApplicationUserId = "u", Amount = 100, Status = PTScheduler.Domain.Enums.OrderStatus.Paid });
            await db.SaveChangesAsync();
        }

        (await svc.SetBuyerAsync(1, new() { Nip = "123", Name = "X" })).Ok.Should().BeFalse();
        (await svc.SetBuyerAsync(1, new() { Nip = "526-025-02-74", Name = "ACME" })).Ok.Should().BeTrue();

        await using var v = f.CreateDbContext();
        (await v.Orders.FindAsync(1))!.BuyerNip.Should().Be("5260250274");
    }

    [Fact]
    public async Task Send_When_Disabled_Explains_What_To_Configure()
    {
        var (svc, f) = Make();
        await using (var db = f.CreateDbContext())
        {
            db.Orders.Add(new PTScheduler.Domain.Entities.Order { Id = 1, ApplicationUserId = "u", Amount = 100, Status = PTScheduler.Domain.Enums.OrderStatus.Paid, InvoiceNumber = "FV/1" });
            await db.SaveChangesAsync();
        }

        var (ok, message) = await svc.SendOrderInvoiceAsync(1);

        ok.Should().BeFalse();
        message.Should().Contain("KSeF");
    }
}
