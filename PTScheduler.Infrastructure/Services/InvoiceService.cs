using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTScheduler.Infrastructure.Services;

public class InvoiceService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IBrandingService brandingService,
    IFinanceService financeService,
    IWebRootPathProvider webRootPathProvider,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public async Task<(byte[] Bytes, string FileName)> GenerateInvoiceAsync(int orderId)
    {
        await using var db = dbFactory.CreateDbContext();
        var order = await db.Orders
            .Include(o => o.Course)
            .Include(o => o.PackageOffer)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException($"Zamówienie {orderId} nie istnieje.");

        if (order.Status != Domain.Enums.OrderStatus.Paid)
            throw new InvalidOperationException("Fakturę można wystawić tylko dla opłaconego zamówienia.");

        var taxCfg = await financeService.GetTaxConfigAsync("standard");

        if (string.IsNullOrEmpty(order.InvoiceNumber))
        {
            if (!taxCfg.InvoiceNumberingEnabled)
                throw new InvalidOperationException("Numeracja faktur jest wyłączona. Włącz ją w ustawieniach podatkowych.");

            var prefix = taxCfg.InvoicePrefix;
            var nextNum = taxCfg.InvoiceNextNumber;
            var yearMonth = DateTime.UtcNow.ToString("yyyy/MM");
            order.InvoiceNumber = $"{prefix}/{nextNum:D4}/{yearMonth}";
            order.InvoiceIssuedAt = DateTime.UtcNow;

            taxCfg.InvoiceNextNumber = nextNum + 1;
            await financeService.SaveTaxConfigAsync(taxCfg);
            await db.SaveChangesAsync();
        }

        var buyer = await userManager.FindByIdAsync(order.ApplicationUserId);
        var branding = await brandingService.GetAsync();

        byte[]? logoBytes = null;
        try
        {
            if (!string.IsNullOrEmpty(branding.LogoPath))
            {
                var rel = branding.LogoPath.TrimStart('/');
                var abs = Path.Combine(webRootPathProvider.WebRootPath, rel);
                if (File.Exists(abs)) logoBytes = await File.ReadAllBytesAsync(abs);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nie udało się wczytać logo ({LogoPath}) na fakturę zamówienia {OrderId} — generuję bez logo.", branding.LogoPath, order.Id);
        }

        var companyName = branding.CompanyName ?? "PTScheduler";
        var itemName = order.Course?.Title ?? order.PackageOffer?.Name ?? order.Description ?? "Usługa";
        var issueDate = order.InvoiceIssuedAt ?? order.PaidAt ?? order.CreatedAt;
        var payDate = order.PaidAt ?? order.CreatedAt;

        var vatEnabled = taxCfg.VatEnabled;
        var vatRate = taxCfg.VatRate;
        var (netAmount, vatAmount) = FinanceMath.SplitVatInclusive(order.Amount, vatEnabled, vatRate);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("DejaVu Sans").FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        if (logoBytes is not null)
                            row.ConstantItem(60).Image(logoBytes).FitArea();
                        else
                            row.ConstantItem(60);

                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().Text("FAKTURA VAT").FontSize(18).Bold().FontColor(Colors.Grey.Darken4);
                            right.Item().Text($"Nr: {order.InvoiceNumber}").FontSize(10).FontColor(Colors.Grey.Medium);
                        });
                    });
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(seller =>
                        {
                            seller.Item().Text("Sprzedawca").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            seller.Item().Text(companyName).FontSize(10).Bold();
                            if (!string.IsNullOrEmpty(taxCfg.SellerNip))
                                seller.Item().Text($"NIP: {taxCfg.SellerNip}").FontSize(9);
                            if (!string.IsNullOrEmpty(taxCfg.SellerAddress))
                                seller.Item().Text(taxCfg.SellerAddress).FontSize(9);
                            if (!string.IsNullOrEmpty(taxCfg.SellerCity))
                            {
                                var city = taxCfg.SellerPostalCode is not null
                                    ? $"{taxCfg.SellerPostalCode} {taxCfg.SellerCity}"
                                    : taxCfg.SellerCity;
                                seller.Item().Text(city).FontSize(9);
                            }
                        });
                        row.RelativeItem().Column(buyerCol =>
                        {
                            buyerCol.Item().Text("Nabywca").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            if (buyer is not null)
                            {
                                var name = $"{buyer.FirstName} {buyer.LastName}".Trim();
                                if (!string.IsNullOrEmpty(name))
                                    buyerCol.Item().Text(name).FontSize(10).Bold();
                                buyerCol.Item().Text(buyer.Email ?? "—").FontSize(9);
                            }
                            else
                            {
                                buyerCol.Item().Text("—").FontSize(10);
                            }
                        });
                    });

                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("Data wystawienia").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            left.Item().Text(issueDate.ToString("dd.MM.yyyy")).FontSize(10);
                        });
                        row.RelativeItem().Column(mid =>
                        {
                            mid.Item().Text("Data sprzedaży").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            mid.Item().Text(payDate.ToString("dd.MM.yyyy")).FontSize(10);
                        });
                        row.RelativeItem().Column(right =>
                        {
                            right.Item().Text("Termin płatności").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            right.Item().Text("Zapłacono").FontSize(10).FontColor(Colors.Green.Darken1);
                        });
                    });

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(30);
                            c.RelativeColumn(4);
                            c.RelativeColumn(1);
                            if (vatEnabled)
                            {
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                            }
                            c.RelativeColumn(1);
                        });

                        void HeaderCell(IContainer c, string text) =>
                            c.PaddingBottom(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                .Text(text).Bold().FontSize(8);

                        table.Header(h =>
                        {
                            HeaderCell(h.Cell(), "Lp.");
                            HeaderCell(h.Cell(), "Nazwa");
                            HeaderCell(h.Cell().AlignRight(), "Ilość");
                            if (vatEnabled)
                            {
                                HeaderCell(h.Cell().AlignRight(), "Netto");
                                HeaderCell(h.Cell().AlignRight(), "VAT %");
                                HeaderCell(h.Cell().AlignRight(), "VAT");
                            }
                            HeaderCell(h.Cell().AlignRight(), "Brutto");
                        });

                        table.Cell().PaddingVertical(6).Text("1").FontSize(9);
                        table.Cell().PaddingVertical(6).Text(itemName);
                        table.Cell().PaddingVertical(6).AlignRight().Text("1");
                        if (vatEnabled)
                        {
                            table.Cell().PaddingVertical(6).AlignRight()
                                .Text($"{netAmount:0.00} {order.Currency}").FontSize(9);
                            table.Cell().PaddingVertical(6).AlignRight()
                                .Text($"{vatRate:0}%").FontSize(9);
                            table.Cell().PaddingVertical(6).AlignRight()
                                .Text($"{vatAmount:0.00} {order.Currency}").FontSize(9);
                        }
                        table.Cell().PaddingVertical(6).AlignRight()
                            .Text($"{order.Amount:0.00} {order.Currency}");
                    });

                    if (order.HasDiscount())
                    {
                        col.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem();
                            row.ConstantItem(220).AlignRight().Column(disc =>
                            {
                                disc.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Kupon:").FontSize(9).FontColor(Colors.Grey.Medium);
                                    r.ConstantItem(100).AlignRight().Text(order.CouponCode ?? "").FontSize(9);
                                });
                                disc.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Rabat:").FontSize(9).FontColor(Colors.Grey.Medium);
                                    r.ConstantItem(100).AlignRight().Text($"-{order.DiscountAmount:0.00} {order.Currency}")
                                        .FontSize(9).FontColor(Colors.Green.Darken1);
                                });
                            });
                        });
                    }

                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(280).AlignRight().Column(summary =>
                        {
                            if (vatEnabled)
                            {
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Razem netto:").FontSize(10);
                                    r.ConstantItem(120).AlignRight().Text($"{netAmount:0.00} {order.Currency}").FontSize(10);
                                });
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text($"VAT ({vatRate:0}%):").FontSize(10);
                                    r.ConstantItem(120).AlignRight().Text($"{vatAmount:0.00} {order.Currency}").FontSize(10);
                                });
                            }
                            summary.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem().Text("Razem brutto:").Bold().FontSize(12);
                                r.ConstantItem(120).AlignRight()
                                    .Text($"{order.Amount:0.00} {order.Currency}").Bold().FontSize(12);
                            });
                        });
                    });

                    col.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem().Column(pay =>
                        {
                            pay.Item().Text("Forma płatności:").FontSize(9).FontColor(Colors.Grey.Medium);
                            pay.Item().Text("Przelew online").FontSize(10);
                        });
                        row.RelativeItem().Column(status =>
                        {
                            status.Item().Text("Status:").FontSize(9).FontColor(Colors.Grey.Medium);
                            status.Item().Text("Opłacono").FontSize(10).FontColor(Colors.Green.Darken1);
                        });
                    });
                });

                page.Footer().AlignCenter().Column(f =>
                {
                    f.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                    f.Item().PaddingTop(4).Text($"Faktura VAT — {companyName}")
                        .FontSize(7).FontColor(Colors.Grey.Lighten1);
                    f.Item().Text("Dokument wygenerowany elektronicznie i jest ważny bez podpisu.")
                        .FontSize(7).FontColor(Colors.Grey.Lighten1);
                });
            });
        });

        var bytes = doc.GeneratePdf();
        var safeNumber = (order.InvoiceNumber ?? order.ExtOrderId).Replace('/', '-');
        var fileName = $"faktura_{safeNumber}.pdf";
        return (bytes, fileName);
    }
}

file static class OrderExtensions
{
    public static bool HasDiscount(this Order o) => o.DiscountAmount.HasValue && o.DiscountAmount > 0;
}
