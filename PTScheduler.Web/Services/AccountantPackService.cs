using System.Globalization;
using System.IO.Compression;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Finance;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static PTScheduler.Application.Finance.AccountantPack;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Paczka dla księgowej”: zestawienie miesiąca (CSV + PDF, razem w ZIP). Do pobrania na stronie albo e-mailem
/// do księgowej jako link ważny <see cref="LinkDays"/> dni (bez logowania, bez załączników — działa przy każdej poczcie).
/// Pakiety ukryte w finansach pomijamy, tak jak w raportach.
/// </summary>
public class AccountantPackService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock, IBrandingService branding,
    IDataProtectionProvider protection, IEmailService email, IEmailTemplateService templates)
{
    public const int LinkDays = 14;
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    private ITimeLimitedDataProtector Protector => protection.CreateProtector("AccountantPack.v1").ToTimeLimitedDataProtector();

    /// <summary>Zestawienie miesiąca. <paramref name="trainerUserId"/> = null — całe studio (właściciel).</summary>
    public async Task<Pack> BuildAsync(int year, int month, string? trainerUserId)
    {
        var fromUtc = clock.ToUtc(new DateTime(year, month, 1));
        var toUtc = clock.ToUtc(new DateTime(year, month, 1).AddMonths(1));
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = new List<Row>();

        // Klienci w zakresie (dla trenera — tylko swoi).
        var clientsQuery = db.Clients.AsNoTracking();
        if (trainerUserId is not null) clientsQuery = clientsQuery.Where(c => c.TrainerUserId == trainerUserId);
        var clientIds = await clientsQuery.Select(c => c.Id).ToListAsync();
        var clientUserIds = (await clientsQuery.Select(c => c.ApplicationUserId).ToListAsync()).ToHashSet();

        // 1) Płatności online.
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid && o.PaidAt >= fromUtc && o.PaidAt < toUtc && o.Provider != PaymentProviders.Simulator)
            .ToListAsync();
        if (trainerUserId is not null) orders = orders.Where(o => clientUserIds.Contains(o.ApplicationUserId)).ToList();
        var userIds = orders.Select(o => o.ApplicationUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
        foreach (var o in orders)
            rows.Add(new Row(clock.ToWallClock(o.PaidAt!.Value), Online, OrderKindLabel(o.Kind), o.Description ?? OrderKindLabel(o.Kind),
                o.BuyerName ?? names.GetValueOrDefault(o.ApplicationUserId, ""), o.Amount, o.Currency, ProviderLabel(o.Provider),
                o.InvoiceNumber, o.BuyerNip, o.KsefNumber));

        // Wszystkie identyfikatory płatności online (także z innych miesięcy) — żeby pakietu z zakupu online nie liczyć drugi raz.
        var onlineRefs = (await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Paid).Select(o => o.ExtOrderId).ToListAsync()).ToHashSet();
        var membershipPackageIds = (await db.MembershipPeriods.AsNoTracking().Where(p => p.PackageId != null).Select(p => p.PackageId!.Value).ToListAsync()).ToHashSet();

        // 2) Pakiety opłacone u trenera.
        var packages = await db.SessionPackages.AsNoTracking().Include(p => p.Client)
            .Where(p => clientIds.Contains(p.ClientId) && !p.IsHidden && p.IsPaid && p.PaidAt >= fromUtc && p.PaidAt < toUtc)
            .ToListAsync();
        foreach (var p in packages.Where(p => (p.PaymentReference is null || !onlineRefs.Contains(p.PaymentReference)) && !membershipPackageIds.Contains(p.Id)))
            rows.Add(new Row(clock.ToWallClock(p.PaidAt!.Value), AtTrainer, "Pakiet", $"{p.Name} ({p.TotalSessions} tr.)",
                $"{p.Client.FirstName} {p.Client.LastName}".Trim(), p.PricePerSession * p.TotalSessions, "PLN", "u trenera"));

        // 3) Pojedyncze treningi poza pakietem opłacone u trenera.
        var sessions = await db.Sessions.AsNoTracking().Include(s => s.Client).Include(s => s.SessionType)
            .Where(s => clientIds.Contains(s.ClientId) && s.PaidAt >= fromUtc && s.PaidAt < toUtc && s.PaidVia != "online")
            .ToListAsync();
        foreach (var s in sessions.Where(s => s.SessionType.SinglePrice is > 0))
            rows.Add(new Row(clock.ToWallClock(s.PaidAt!.Value), AtTrainer, "Trening", $"{s.SessionType.Name} {s.StartTime:dd.MM HH:mm}",
                $"{s.Client.FirstName} {s.Client.LastName}".Trim(), s.SessionType.SinglePrice!.Value, "PLN", PaidViaLabel(s.PaidVia)));

        // 4) Karnety opłacone poza płatnością online.
        var periods = await db.MembershipPeriods.AsNoTracking().Include(p => p.Membership).ThenInclude(m => m.Client).Include(p => p.Membership).ThenInclude(m => m.Plan)
            .Where(p => clientIds.Contains(p.Membership.ClientId) && p.PaidAt >= fromUtc && p.PaidAt < toUtc)
            .ToListAsync();
        foreach (var p in periods.Where(p => p.PaymentReference is null || !onlineRefs.Contains(p.PaymentReference)))
            rows.Add(new Row(clock.ToWallClock(p.PaidAt!.Value), AtTrainer, "Karnet", $"{p.Membership.Plan.Name} {p.PeriodStart:dd.MM}–{p.PeriodEnd:dd.MM}",
                $"{p.Membership.Client.FirstName} {p.Membership.Client.LastName}".Trim(), p.Amount, "PLN", "u trenera"));

        // 5) Informacyjnie: sprzedane w tym miesiącu, jeszcze nieopłacone pakiety.
        var unpaid = await db.SessionPackages.AsNoTracking().Include(p => p.Client)
            .Where(p => clientIds.Contains(p.ClientId) && !p.IsHidden && !p.IsPaid && p.Status != PackageStatus.Cancelled
                     && p.PurchasedAt >= fromUtc && p.PurchasedAt < toUtc)
            .ToListAsync();
        foreach (var p in unpaid)
            rows.Add(new Row(clock.ToWallClock(p.PurchasedAt), Unpaid, "Pakiet", $"{p.Name} ({p.TotalSessions} tr.)",
                $"{p.Client.FirstName} {p.Client.LastName}".Trim(), p.PricePerSession * p.TotalSessions, "PLN", "—"));

        var tax = await db.FinanceTaxConfigs.AsNoTracking().FirstOrDefaultAsync(t => t.Module == "standard");
        var seller = !string.IsNullOrWhiteSpace(tax?.SellerName) ? tax!.SellerName! : (await branding.GetAsync()).CompanyName;
        if (!string.IsNullOrWhiteSpace(tax?.SellerNip)) seller += $", NIP {tax!.SellerNip}";
        return new Pack(year, month, seller, rows);
    }

    public static string OrderKindLabel(OrderKind k) => k switch
    {
        OrderKind.Package => "Pakiet",
        OrderKind.Membership => "Karnet",
        OrderKind.GiftVoucher => "Bon podarunkowy",
        OrderKind.Session => "Trening",
        OrderKind.Course => "Kurs online",
        _ => k.ToString()
    };

    private static string ProviderLabel(string p) => p switch
    {
        PaymentProviders.PayU => "PayU",
        PaymentProviders.Przelewy24 => "Przelewy24",
        PaymentProviders.AutoPay => "Autopay",
        PaymentProviders.Klarna => "Klarna",
        _ => string.IsNullOrEmpty(p) ? "online" : char.ToUpper(p[0]) + p[1..]
    };

    private static string PaidViaLabel(string? via) => via switch
    {
        "cash" => "gotówka",
        "transfer" => "przelew",
        "card" => "karta",
        null or "" => "u trenera",
        _ => via
    };

    /// <summary>PDF: nagłówek z miesiącem i sprzedawcą, podsumowanie, trzy tabele.</summary>
    public byte[] Pdf(Pack pack) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily("DejaVu Sans").FontColor(Colors.Grey.Darken3));
            page.Header().Column(col =>
            {
                col.Item().Text($"Zestawienie dla księgowej — {pack.MonthName}").FontSize(16).Bold().FontColor(Colors.Grey.Darken4);
                col.Item().Text(pack.Seller).FontSize(10).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(6).Row(r =>
                {
                    Stat(r, "Płatności online", Money(pack.Total(Online), pack.Currency));
                    Stat(r, "Opłacone u trenera", Money(pack.Total(AtTrainer), pack.Currency));
                    Stat(r, "Wpływy razem", Money(pack.Received, pack.Currency));
                    Stat(r, "Faktury", pack.Invoices.ToString());
                });
                col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });
            page.Content().Column(col =>
            {
                foreach (var group in new[] { Online, AtTrainer, Unpaid })
                {
                    var list = pack.InGroup(group).ToList();
                    col.Item().PaddingTop(10).Text($"{group} — {Money(pack.Total(group), pack.Currency)}").FontSize(11).Bold();
                    if (list.Count == 0) { col.Item().Text("Brak pozycji.").FontColor(Colors.Grey.Medium); continue; }
                    col.Item().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(62); c.ConstantColumn(70); c.RelativeColumn(3); c.RelativeColumn(2);
                            c.ConstantColumn(70); c.ConstantColumn(80); c.RelativeColumn(2);
                        });
                        t.Header(h =>
                        {
                            foreach (var title in new[] { "Data", "Rodzaj", "Opis", "Klient", "Kwota", "Sposób", "Faktura / KSeF" })
                                h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(title).Bold();
                        });
                        foreach (var r in list)
                        {
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(r.Date.ToString("dd.MM.yyyy"));
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(r.Kind);
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(r.Description);
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(r.Client);
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight().Text(r.Amount.ToString("N2", Pl));
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(r.Method);
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text(string.Join(" · ", new[] { r.Invoice, r.BuyerNip is null ? null : $"NIP {r.BuyerNip}", r.Ksef }.Where(x => !string.IsNullOrEmpty(x))));
                        }
                    });
                }
            });
            page.Footer().AlignRight().Text(t =>
            {
                t.Span($"Wygenerowano {clock.LocalNow:dd.MM.yyyy HH:mm} · strona ").FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontColor(Colors.Grey.Medium);
            });
        });
    }).GeneratePdf();

    private static void Stat(RowDescriptor r, string label, string value) => r.RelativeItem().Column(c =>
    {
        c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Medium);
        c.Item().Text(value).FontSize(12).Bold();
    });

    /// <summary>ZIP z CSV i PDF — jeden plik do wysłania albo pobrania.</summary>
    public byte[] Zip(Pack pack)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, $"{pack.FileStem}.csv", Csv(pack));
            Add(zip, $"{pack.FileStem}.pdf", Pdf(pack));
        }
        return ms.ToArray();

        static void Add(ZipArchive zip, string name, byte[] bytes)
        {
            using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            s.Write(bytes);
        }
    }

    // ── Link dla księgowej ────────────────────────────────────────────────────────────────

    public string CreateLink(int year, int month, string? trainerUserId) =>
        AppUrls.Absolute($"/ksiegowa/{Protector.Protect($"{year}|{month}|{trainerUserId ?? "*"}", TimeSpan.FromDays(LinkDays))}");

    public bool TryReadLink(string token, out int year, out int month, out string? trainerUserId)
    {
        year = month = 0; trainerUserId = null;
        try
        {
            var parts = Protector.Unprotect(token).Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[0], out year) || !int.TryParse(parts[1], out month) || month is < 1 or > 12) return false;
            trainerUserId = parts[2] == "*" ? null : parts[2];
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }

    // ── Ustawienia i wysyłka ──────────────────────────────────────────────────────────────

    public sealed record Settings(string? Email, bool AutoSend, string? LastSentMonth);

    public async Task<Settings> GetSettingsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.FinanceTaxConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.Module == "standard");
        return new(t?.AccountantEmail, t?.AccountantAutoSend ?? false, t?.AccountantLastSentMonth);
    }

    public async Task SaveSettingsAsync(string? accountantEmail, bool autoSend)
    {
        var mail = accountantEmail?.Trim();
        if (!string.IsNullOrEmpty(mail) && !System.Net.Mail.MailAddress.TryCreate(mail, out _))
            throw new ArgumentException("To nie wygląda na adres e-mail.");
        if (autoSend && string.IsNullOrEmpty(mail)) throw new ArgumentException("Podaj adres księgowej, żeby wysyłać co miesiąc.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.FinanceTaxConfigs.FirstOrDefaultAsync(x => x.Module == "standard");
        if (t is null) db.FinanceTaxConfigs.Add(t = new FinanceTaxConfig { Module = "standard" });
        t.AccountantEmail = string.IsNullOrEmpty(mail) ? null : mail;
        t.AccountantAutoSend = autoSend;
        await db.SaveChangesAsync();
    }

    public async Task MarkAutoSentAsync(int year, int month)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.FinanceTaxConfigs.FirstOrDefaultAsync(x => x.Module == "standard");
        if (t is null) return;
        t.AccountantLastSentMonth = $"{year}-{month:00}";
        await db.SaveChangesAsync();
    }

    /// <summary>E-mail do księgowej z podsumowaniem i linkiem do ZIP-a (ważny 14 dni).</summary>
    public async Task<(bool Ok, string? Error)> SendAsync(int year, int month, string? trainerUserId, string to)
    {
        if (!await email.IsEnabledAsync()) return (false, "Wysyłka e-maili nie jest skonfigurowana.");
        var pack = await BuildAsync(year, month, trainerUserId);
        var (subject, html) = await templates.RenderAsync("accountant-pack", new Dictionary<string, string>
        {
            ["Month"] = pack.MonthName,
            ["Seller"] = pack.Seller,
            ["OnlineTotal"] = Money(pack.Total(Online), pack.Currency),
            ["AtTrainerTotal"] = Money(pack.Total(AtTrainer), pack.Currency),
            ["ReceivedTotal"] = Money(pack.Received, pack.Currency),
            ["InvoiceCount"] = pack.Invoices.ToString(),
            ["DownloadLink"] = CreateLink(year, month, trainerUserId),
            ["ValidDays"] = LinkDays.ToString()
        });
        try
        {
            await email.SendAsync(to, "", subject, html);
            return (true, null);
        }
        catch (Exception ex) { return (false, $"Nie udało się wysłać: {ex.Message}"); }
    }
}
