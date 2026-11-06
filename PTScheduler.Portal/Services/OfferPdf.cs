using PTScheduler.Portal.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTScheduler.Portal.Services;

/// <summary>
/// PDF „Oferta i zestawienie usług” dla trenera: abonament, zamówione dodatki, gratisy, dodatkowe opłaty
/// i podsumowanie kwot. Font Lato jest w paczce QuestPDF, więc polskie znaki działają bez fontów systemowych.
/// </summary>
public static class OfferPdf
{
    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";
    private const string Accent = "#4f46e5";
    private const string Ok = "#047857";

    static OfferPdf() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Render(OfferSummary o, string? sellerDetails, string platformName)
    {
        var tz = FindWarsaw();
        string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz).ToString("dd.MM.yyyy");
        string Period(TenantOfferItem i) =>
            i.EndsAt is { } end ? $"{Date(i.StartsAt)} – {OfferService.LastDayLocal(end, tz):dd.MM.yyyy}" : $"od {Date(i.StartsAt)}";

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9.5f).FontColor(Ink));

            page.Header().Column(h =>
            {
                h.Item().Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text(platformName).FontSize(18).Bold().FontColor(Accent);
                        if (!string.IsNullOrWhiteSpace(sellerDetails))
                            c.Item().PaddingTop(2).Text(sellerDetails.Trim()).FontSize(8.5f).FontColor(Muted);
                    });
                    r.ConstantItem(210).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("Oferta i zestawienie usług").FontSize(13).Bold();
                        c.Item().AlignRight().Text($"Stan na {Date(o.GeneratedAt)}").FontColor(Muted);
                    });
                });
                h.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Line);
            });

            page.Content().Column(col =>
            {
                col.Spacing(14);

                // Trener
                col.Item().Background("#f8fafc").Border(1).BorderColor(Line).Padding(10).Row(r =>
                {
                    r.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Dla").FontSize(8).FontColor(Muted);
                        c.Item().Text(string.IsNullOrWhiteSpace(o.Tenant.CompanyName) ? o.Tenant.OwnerName : o.Tenant.CompanyName).Bold().FontSize(11);
                        c.Item().Text(o.Tenant.OwnerName);
                        c.Item().Text(o.Tenant.OwnerEmail).FontColor(Muted);
                    });
                    r.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("Aplikacja").FontSize(8).FontColor(Muted);
                        c.Item().AlignRight().Text(o.Tenant.Domain);
                        c.Item().AlignRight().Text($"Klient od {Date(o.Tenant.CreatedAt)}").FontColor(Muted);
                    });
                });

                // Podsumowanie
                col.Item().Row(r =>
                {
                    r.Spacing(8);
                    Tile(r.RelativeItem(), "Co miesiąc", OfferService.Money(o.MonthlyTotal));
                    Tile(r.RelativeItem(), "Co rok", OfferService.Money(o.YearlyTotal));
                    Tile(r.RelativeItem(), "Jednorazowo do rozliczenia", OfferService.Money(o.OneTimeDue.Sum(c => c.Total)));
                    Tile(r.RelativeItem(), "Wartość gratisów", o.GiftValueMonthly + o.GiftValueOneTime > 0
                        ? OfferService.Money(o.GiftValueMonthly + o.GiftValueOneTime) : $"{o.ActiveGifts.Count}", Ok);
                });

                // Abonament
                col.Item().Element(c => Section(c, "Abonament", t =>
                {
                    Head(t, "Plan", "Rozliczenie", "Kwota");
                    Row(t, o.Plan?.Name ?? o.Tenant.PlanId, PlanDetails(o), OfferService.BillingLabel(o.PlanCycle), OfferService.Money(o.PlanAmount));
                }));

                if (o.Addons.Count > 0)
                    col.Item().Element(c => Section(c, "Dodatki zamówione przez trenera", t =>
                    {
                        Head(t, "Dodatek", "Rozliczenie", "Kwota");
                        foreach (var a in o.Addons)
                            Row(t, a.Name + (a.Quantity > 1 ? $" × {a.Quantity}" : ""), $"od {Date(a.Since)}",
                                a.ViaCard ? "Co miesiąc (karta)" : "Co miesiąc", OfferService.Money(a.Total));
                    }));

                var orders = o.Orders.Where(x => x.Status != ServiceOrderStatus.Cancelled).Take(30).ToList();
                if (orders.Count > 0)
                    col.Item().Element(c => Section(c, "Zamówienia ze sklepu", t =>
                    {
                        Head(t, "Usługa", "Status", "Kwota");
                        foreach (var x in orders)
                            Row(t, x.ServiceItem?.Name ?? "Usługa", Date(x.CreatedAt), OrderStatus(x.Status), OfferService.Money(x.Price));
                    }));

                if (o.Gifts.Count > 0)
                    col.Item().Element(c => Section(c, "W gratisie", t =>
                    {
                        Head(t, "Pozycja", "Okres", "Wartość");
                        foreach (var g in o.Gifts.Where(g => g.CancelledAt is null || g.Billing == OfferBilling.OneTime))
                            Row(t, g.Name + (g.Quantity > 1 ? $" × {g.Quantity}" : ""),
                                Join(g.Description, OfferService.EffectLabel(g)),
                                g.Billing == OfferBilling.OneTime ? Date(g.StartsAt) : $"{OfferService.BillingLabel(g.Billing)}, {Period(g)}",
                                g.Total > 0 ? $"0 zł (zamiast {OfferService.Money(g.Total)}{OfferService.BillingSuffix(g.Billing)})" : "0 zł",
                                Ok);
                    }));

                var charges = o.Charges.Where(c => c.CancelledAt is null || c.Billing == OfferBilling.OneTime).ToList();
                if (charges.Count > 0)
                    col.Item().Element(c => Section(c, "Usługi dodatkowo płatne", t =>
                    {
                        Head(t, "Pozycja", "Rozliczenie", "Kwota");
                        foreach (var x in charges)
                            Row(t, x.Name + (x.Quantity > 1 ? $" × {x.Quantity} po {OfferService.Money(x.UnitPrice)}" : ""),
                                Join(x.Description, OfferService.EffectLabel(x)),
                                x.Billing == OfferBilling.OneTime
                                    ? (x.SettledAt is { } s ? $"Jednorazowo, rozliczone {Date(s)}" : "Jednorazowo, do rozliczenia")
                                    : $"{OfferService.BillingLabel(x.Billing)}, {Period(x)}",
                                OfferService.Money(x.Total) + OfferService.BillingSuffix(x.Billing));
                    }));

                // SMS
                col.Item().Element(c => Section(c, "SMS", t =>
                {
                    Head(t, "Pozycja", "", "Stan");
                    Row(t, "Limit miesięczny (plan i oferta)", null, "",
                        o.Sms.Unlimited ? "bez limitu" : $"{o.Sms.MonthlyUsed} / {o.Sms.MonthlyLimit} w tym miesiącu");
                    Row(t, "SMS dokupione i w gratisie", "Nie wygasają, używane po wyczerpaniu limitu", "", $"{o.Sms.Credits:0}");
                }));

                col.Item().PaddingTop(4).Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                    t.Span("Kwoty w złotych. Opłaty cykliczne naliczane są w każdym okresie rozliczeniowym do czasu rezygnacji; ");
                    t.Span("pozycje jednorazowe „do rozliczenia” zostaną doliczone do najbliższego rachunku. Zestawienie nie jest fakturą.");
                });
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                t.Span($"{platformName} · strona ");
                t.CurrentPageNumber();
                t.Span(" z ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static void Tile(IContainer c, string label, string value, string color = Ink) =>
        c.Border(1).BorderColor(Line).Padding(8).Column(col =>
        {
            col.Item().Text(label).FontSize(7.5f).FontColor(Muted);
            col.Item().Text(value).FontSize(13).Bold().FontColor(color);
        });

    private static void Section(IContainer c, string title, Action<TableDescriptor> body) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(4).Text(title).FontSize(11).Bold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(5);
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(3);
                });
                body(t);
            });
        });

    private static void Head(TableDescriptor t, string a, string b, string c)
    {
        foreach (var (text, right) in new[] { (a, false), (b, false), (c, true) })
        {
            var cell = t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(3);
            (right ? cell.AlignRight() : cell).Text(text).FontSize(7.5f).FontColor(Muted).Bold();
        }
    }

    private static void Row(TableDescriptor t, string name, string? detail, string billing, string amount, string amountColor = Ink)
    {
        t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(4).Column(c =>
        {
            c.Item().Text(name).SemiBold();
            if (!string.IsNullOrWhiteSpace(detail)) c.Item().Text(detail).FontSize(8).FontColor(Muted);
        });
        t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(4).Text(billing).FontColor(Muted);
        t.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(4).AlignRight().Text(amount).SemiBold().FontColor(amountColor);
    }

    private static string? Join(params string?[] parts)
    {
        var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return list.Count == 0 ? null : string.Join(" · ", list);
    }

    private static string PlanDetails(OfferSummary o)
    {
        if (o.Plan is null) return "";
        var parts = new List<string>();
        if (o.Plan.MaxClients != int.MaxValue) parts.Add($"do {o.Plan.MaxClients} podopiecznych");
        else parts.Add("bez limitu podopiecznych");
        if (o.Sms.MonthlyLimit > 0) parts.Add(o.Sms.Unlimited ? "SMS bez limitu" : $"{o.Sms.MonthlyLimit} SMS / mies.");
        return string.Join(" · ", parts);
    }

    public static string OrderStatus(ServiceOrderStatus s) => s switch
    {
        ServiceOrderStatus.Pending => "Nowe",
        ServiceOrderStatus.Accepted => "Przyjęte",
        ServiceOrderStatus.InProgress => "W realizacji",
        ServiceOrderStatus.Completed => "Zrealizowane",
        ServiceOrderStatus.Cancelled => "Anulowane",
        ServiceOrderStatus.AwaitingPayment => "Czeka na płatność",
        _ => s.ToString()
    };

    private static TimeZoneInfo FindWarsaw()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch { return TimeZoneInfo.Utc; }
    }
}
