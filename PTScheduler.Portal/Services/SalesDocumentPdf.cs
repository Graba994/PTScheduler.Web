using PTScheduler.Portal.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTScheduler.Portal.Services;

/// <summary>
/// PDF dokumentu sprzedaży (rachunek, faktura bez VAT, faktura VAT, korekta) z danych zapisanych w dokumencie —
/// ten sam dokument zawsze wygląda tak samo, niezależnie od późniejszych zmian ustawień.
/// </summary>
public static class SalesDocumentPdf
{
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";
    private const string Accent = "#4f46e5";
    private const string Danger = "#b91c1c";

    static SalesDocumentPdf() => QuestPDF.Settings.License = LicenseType.Community;

    private static string M(decimal v) => FinanceRules.Money(v);

    public static byte[] Render(SalesDocument d)
    {
        var vat = d.Kind == SalesDocumentKinds.InvoiceVat || d.Lines.Any(l => int.TryParse(l.VatRate, out var r) && r > 0);
        var title = d.Kind switch
        {
            SalesDocumentKinds.Correction => d.Corrects?.Kind == SalesDocumentKinds.Receipt ? "Rachunek korygujący" : "Faktura korygująca",
            SalesDocumentKinds.InvoiceExempt => "Faktura",
            SalesDocumentKinds.InvoiceVat => "Faktura VAT",
            _ => "Rachunek"
        };

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9.5f).FontColor(Ink));

            page.Header().Column(h =>
            {
                h.Item().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text(title).FontSize(20).Bold().FontColor(Accent);
                        c.Item().Text(string.IsNullOrEmpty(d.Number) ? "numer w przygotowaniu" : $"nr {d.Number}").FontSize(12).SemiBold();
                        if (d.Status == SalesDocumentStatus.Cancelled)
                            c.Item().PaddingTop(4).Text($"ANULOWANY — {d.CancelReason}").FontColor(Danger).Bold();
                    });
                    r.ConstantItem(200).AlignRight().Column(c =>
                    {
                        Kv(c, "Data wystawienia", d.IssueDate.ToString("dd.MM.yyyy"));
                        Kv(c, "Data sprzedaży", d.SaleDate.ToString("dd.MM.yyyy"));
                        if (d.PaidDate is { } paid) Kv(c, "Zapłacono", paid.ToString("dd.MM.yyyy"));
                        if (!string.IsNullOrWhiteSpace(d.PaymentMethod)) Kv(c, "Sposób zapłaty", d.PaymentMethod!);
                    });
                });
                h.Item().PaddingVertical(12).LineHorizontal(1).LineColor(Line);
            });

            page.Content().Column(col =>
            {
                col.Spacing(14);

                col.Item().Row(r =>
                {
                    r.Spacing(14);
                    Party(r.RelativeItem(), "Sprzedawca", d.Seller.Split('\n'));
                    var buyer = new List<string> { d.BuyerName };
                    buyer.AddRange((d.BuyerAddress ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    if (!string.IsNullOrWhiteSpace(d.BuyerTaxId)) buyer.Add($"NIP: {d.BuyerTaxId}");
                    Party(r.RelativeItem(), "Nabywca", buyer);
                });

                if (d.Kind == SalesDocumentKinds.Correction)
                    col.Item().Background("#fef2f2").Padding(10).Column(c =>
                    {
                        c.Item().Text($"Dotyczy dokumentu {d.Corrects?.Number} z {d.Corrects?.IssueDate:dd.MM.yyyy}").SemiBold();
                        if (!string.IsNullOrWhiteSpace(d.CorrectionReason)) c.Item().Text($"Przyczyna korekty: {d.CorrectionReason}");
                    });

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22);
                        c.RelativeColumn(6);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(2);
                        if (vat) { c.RelativeColumn(1.2f); c.RelativeColumn(2); c.RelativeColumn(1.8f); }
                        c.RelativeColumn(2);
                    });
                    var heads = vat
                        ? new[] { "Lp.", "Nazwa", "Ilość", "Cena brutto", "VAT", "Netto", "Kwota VAT", "Brutto" }
                        : new[] { "Lp.", "Nazwa", "Ilość", "Cena", "Wartość" };
                    for (var i = 0; i < heads.Length; i++)
                    {
                        var cell = t.Cell().BorderBottom(1).BorderColor(Ink).PaddingVertical(4);
                        (i >= 2 ? cell.AlignRight() : cell).Text(heads[i]).FontSize(8).Bold().FontColor(Muted);
                    }
                    var no = 0;
                    foreach (var l in d.Lines)
                    {
                        no++;
                        Cell(t).Text(no.ToString()).FontColor(Muted);
                        Cell(t).Text(l.Name);
                        Cell(t).AlignRight().Text($"{l.Quantity:0.###} {l.Unit}");
                        Cell(t).AlignRight().Text(M(l.UnitGross));
                        if (vat)
                        {
                            Cell(t).AlignRight().Text(int.TryParse(l.VatRate, out var r) ? $"{r}%" : l.VatRate);
                            Cell(t).AlignRight().Text(M(l.Net));
                            Cell(t).AlignRight().Text(M(l.Vat));
                        }
                        Cell(t).AlignRight().Text(M(l.Gross)).SemiBold();
                    }
                });

                col.Item().AlignRight().Width(250).Column(c =>
                {
                    if (vat)
                    {
                        Sum(c, "Razem netto", M(d.Net));
                        Sum(c, "VAT", M(d.Vat));
                    }
                    c.Item().PaddingTop(4).BorderTop(1).BorderColor(Ink).PaddingTop(6).Row(r =>
                    {
                        r.RelativeItem().Text(d.Gross < 0 ? "Do zwrotu" : d.PaidDate is null ? "Do zapłaty" : "Razem (zapłacono)").Bold();
                        r.AutoItem().Text(M(Math.Abs(d.Gross))).FontSize(14).Bold().FontColor(Accent);
                    });
                    c.Item().AlignRight().Text("Słownie: " + FinanceRules.InWords(Math.Abs(d.Gross))).FontSize(8).FontColor(Muted);
                });

                if (!string.IsNullOrWhiteSpace(d.VatNote))
                    col.Item().Border(1).BorderColor(Line).Padding(8).Text(d.VatNote).FontSize(8.5f);
                if (!string.IsNullOrWhiteSpace(d.Note))
                    col.Item().Text(d.Note).FontSize(8.5f).FontColor(Muted);
            });

            page.Footer().Column(f =>
            {
                f.Item().PaddingBottom(4).Text("Dokument wystawiony elektronicznie — nie wymaga podpisu.").FontSize(7.5f).FontColor(Muted);
                f.Item().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                    t.Span($"{title} {d.Number} · strona ");
                    t.CurrentPageNumber();
                    t.Span(" z ");
                    t.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    private static IContainer Cell(TableDescriptor t) => t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(5);

    private static void Kv(ColumnDescriptor c, string k, string v) => c.Item().AlignRight().Text(t =>
    {
        t.Span(k + ": ").FontColor(Muted).FontSize(8.5f);
        t.Span(v).SemiBold();
    });

    private static void Sum(ColumnDescriptor c, string k, string v) => c.Item().Row(r =>
    {
        r.RelativeItem().Text(k).FontColor(Muted);
        r.AutoItem().Text(v);
    });

    private static void Party(IContainer c, string label, IEnumerable<string> lines) =>
        c.Background("#f8fafc").Border(1).BorderColor(Line).Padding(10).Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Muted).Bold();
            var first = true;
            foreach (var l in lines.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var text = col.Item().Text(l.Trim());
                if (first) text.SemiBold().FontSize(10.5f);
                first = false;
            }
        });
}

/// <summary>Uproszczona ewidencja sprzedaży (dziennie, narastająco w miesiącu) do wydruku i archiwum.</summary>
public static class LedgerPdf
{
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";

    static LedgerPdf() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Render(List<AccountingService.LedgerDay> days, DateOnly from, DateOnly to, string seller, string mode)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9).FontColor(Ink));

            page.Header().Column(h =>
            {
                h.Item().Text("Uproszczona ewidencja sprzedaży").FontSize(16).Bold();
                h.Item().Text($"{from:dd.MM.yyyy} – {to:dd.MM.yyyy} · {mode}").FontColor(Muted);
                h.Item().PaddingTop(4).Text(seller.Replace('\n', ' ').Trim()).FontSize(8).FontColor(Muted);
                h.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Line);
            });

            page.Content().Column(col =>
            {
                col.Spacing(12);
                if (days.Count == 0)
                    col.Item().Text("Brak sprzedaży w tym okresie.").FontColor(Muted);
                foreach (var month in days.GroupBy(d => (d.Date.Year, d.Date.Month)))
                {
                    col.Item().Column(m =>
                    {
                        m.Item().PaddingBottom(4).Text(new DateTime(month.Key.Year, month.Key.Month, 1).ToString("MMMM yyyy", FinanceRules.Pl)).FontSize(11).Bold();
                        m.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(28);
                                c.ConstantColumn(64);
                                c.RelativeColumn(6);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2.4f);
                            });
                            foreach (var (text, right) in new[] { ("Lp.", false), ("Data", false), ("Sprzedaż", false), ("Kwota z dnia", true), ("Narastająco", true) })
                            {
                                var cell = t.Cell().BorderBottom(1).BorderColor(Ink).PaddingVertical(3);
                                (right ? cell.AlignRight() : cell).Text(text).FontSize(7.5f).Bold().FontColor(Muted);
                            }
                            foreach (var d in month)
                            {
                                t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(3).Text(d.No.ToString()).FontColor(Muted);
                                t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(3).Text(d.Date.ToString("dd.MM.yyyy"));
                                t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(3).Text(string.Join("; ", d.Entries.Select(e => e.Description))).FontSize(8);
                                t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(3).AlignRight().Text(FinanceRules.Money(d.Amount));
                                t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(3).AlignRight().Text(FinanceRules.Money(d.MonthToDate)).SemiBold();
                            }
                        });
                        m.Item().AlignRight().PaddingTop(3).Text($"Razem w miesiącu: {FinanceRules.Money(month.Sum(d => d.Amount))}").Bold();
                    });
                }
                col.Item().PaddingTop(6).AlignRight().Text($"Razem w okresie: {FinanceRules.Money(days.Sum(d => d.Amount))}").FontSize(11).Bold();
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                t.Span("Ewidencja z Hantly · strona ");
                t.CurrentPageNumber();
                t.Span(" z ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }
}
