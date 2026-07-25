using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTScheduler.Infrastructure.Services;

public class ReceiptService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IBrandingService brandingService,
    IWebRootPathProvider webRootPathProvider) : IReceiptService
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public async Task<(byte[] Bytes, string FileName)> GenerateReceiptAsync(int orderId)
    {
        await using var db = dbFactory.CreateDbContext();
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Course)
            .Include(o => o.PackageOffer)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException($"Zamówienie {orderId} nie istnieje.");

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
        catch { }

        var companyName = branding.CompanyName ?? "PTScheduler";
        var itemName = order.Course?.Title ?? order.PackageOffer?.Name ?? order.Description ?? "Zamówienie";
        var paidDate = order.PaidAt ?? order.CreatedAt;

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
                            right.Item().Text(companyName).FontSize(16).Bold().FontColor(Colors.Grey.Darken4);
                            right.Item().Text($"Potwierdzenie nr {order.ExtOrderId}").FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                    });
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("Kupujący").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            left.Item().Text(buyer?.Email ?? "—").FontSize(10);
                        });
                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().Text("Data płatności").Bold().FontSize(9).FontColor(Colors.Grey.Medium);
                            right.Item().Text(paidDate.ToString("dd MMMM yyyy, HH:mm", Pl)).FontSize(10);
                        });
                    });

                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });

                        table.Header(h =>
                        {
                            h.Cell().PaddingBottom(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                .Text("Produkt").Bold().FontSize(9);
                            h.Cell().PaddingBottom(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                .AlignRight().Text("Ilość").Bold().FontSize(9);
                            h.Cell().PaddingBottom(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                                .AlignRight().Text("Kwota").Bold().FontSize(9);
                        });

                        table.Cell().PaddingVertical(6).Text(itemName);
                        table.Cell().PaddingVertical(6).AlignRight().Text("1");
                        if (order.HasDiscount())
                        {
                            table.Cell().PaddingVertical(6).AlignRight().Column(c =>
                            {
                                c.Item().Text($"{order.OriginalAmount:0.00} {order.Currency}")
                                    .FontSize(9).Strikethrough().FontColor(Colors.Grey.Medium);
                                c.Item().Text($"{order.Amount:0.00} {order.Currency}");
                            });
                        }
                        else
                        {
                            table.Cell().PaddingVertical(6).AlignRight()
                                .Text($"{order.Amount:0.00} {order.Currency}");
                        }
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
                        row.ConstantItem(220).AlignRight().Row(total =>
                        {
                            total.RelativeItem().Text("Do zapłaty:").Bold().FontSize(12);
                            total.ConstantItem(120).AlignRight()
                                .Text($"{order.Amount:0.00} {order.Currency}").Bold().FontSize(12);
                        });
                    });

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(220).AlignRight().Row(st =>
                        {
                            st.RelativeItem().Text("Status:").FontSize(9).FontColor(Colors.Grey.Medium);
                            st.ConstantItem(120).AlignRight()
                                .Text(order.Status == Domain.Enums.OrderStatus.Paid ? "Opłacone" : order.Status.ToString())
                                .FontSize(9).FontColor(
                                    order.Status == Domain.Enums.OrderStatus.Paid
                                        ? Colors.Green.Darken1
                                        : Colors.Orange.Darken1);
                        });
                    });

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(220).AlignRight().Row(prov =>
                        {
                            prov.RelativeItem().Text("Metoda płatności:").FontSize(9).FontColor(Colors.Grey.Medium);
                            prov.ConstantItem(120).AlignRight()
                                .Text(order.Provider).FontSize(9);
                        });
                    });
                });

                page.Footer().AlignCenter().Column(f =>
                {
                    f.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                    f.Item().PaddingTop(4).Text($"Wygenerowano automatycznie — {companyName}")
                        .FontSize(7).FontColor(Colors.Grey.Lighten1);
                    f.Item().Text("Dokument nie stanowi faktury VAT.")
                        .FontSize(7).FontColor(Colors.Grey.Lighten1);
                });
            });
        });

        var bytes = doc.GeneratePdf();
        var fileName = $"potwierdzenie_{order.ExtOrderId}.pdf";
        return (bytes, fileName);
    }
}

file static class OrderExtensions
{
    public static bool HasDiscount(this Order o) => o.DiscountAmount.HasValue && o.DiscountAmount > 0;
}
