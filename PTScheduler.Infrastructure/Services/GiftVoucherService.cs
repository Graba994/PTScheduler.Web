using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Marketing;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTScheduler.Infrastructure.Services;

public class GiftVoucherService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IMarketingSettingsService settings,
    IBrandingService branding,
    IWebRootPathProvider webRoot,
    IAppClock clock,
    ILogger<GiftVoucherService> logger) : IGiftVoucherService
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    private static string NewCode()
    {
        string Part() => string.Create(4, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
        return $"BON-{Part()}-{Part()}";
    }

    private async Task<string> UniqueCodeAsync(ApplicationDbContext db)
    {
        for (var i = 0; i < 10; i++)
        {
            var code = NewCode();
            if (!await db.GiftVouchers.AnyAsync(v => v.Code == code) && !await db.Coupons.AnyAsync(c => c.Code == code))
                return code;
        }
        throw new InvalidOperationException("Nie udało się wygenerować kodu bonu.");
    }

    /// <summary>Buduje bon z żądania — sprawdza kwotę albo pakiet.</summary>
    private async Task<(GiftVoucher? Voucher, string? Error)> BuildAsync(ApplicationDbContext db, GiftVoucherRequest r, bool manual)
    {
        var cfg = await settings.GetAsync();
        var voucher = new GiftVoucher
        {
            Kind = r.Kind,
            RecipientName = Clip(r.RecipientName, 80),
            FromName = Clip(r.FromName, 80),
            Message = Clip(r.Message, 300),
            CreatedAt = clock.UtcNow,
            IssuedManually = manual
        };

        if (r.Kind == GiftVoucherKind.Amount)
        {
            var amount = Math.Round(r.Amount, 2);
            // Online tylko kwoty z listy trenera; ręcznie — dowolna rozsądna kwota.
            if (manual ? amount is < 1 or > 10000 : !cfg.VoucherAmountList.Contains(amount))
                return (null, "Wybierz kwotę bonu.");
            voucher.Value = amount;
            voucher.Title = $"Bon {amount.ToString("0.##", Pl)} zł";
        }
        else
        {
            var offer = await db.PackageOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == r.PackageOfferId);
            if (offer is null || (!manual && !offer.IsActive)) return (null, "Wybierz pakiet.");
            voucher.Value = offer.Price;
            voucher.Currency = offer.Currency;
            voucher.Title = offer.Name;
            voucher.PackageOfferId = offer.Id;
            voucher.SessionTypeId = offer.SessionTypeId;
            voucher.SessionsCount = offer.SessionsCount;
            voucher.PackageValidDays = offer.ValidDays;
        }
        voucher.Code = await UniqueCodeAsync(db);
        return (voucher, null);
    }

    public async Task<(bool Ok, string? Error, int? VoucherId)> CreatePendingAsync(string buyerUserId, GiftVoucherRequest request)
    {
        var cfg = await settings.GetAsync();
        if (!cfg.VouchersEnabled) return (false, "Bony podarunkowe są wyłączone.", null);

        await using var db = dbFactory.CreateDbContext();
        var (voucher, error) = await BuildAsync(db, request, manual: false);
        if (voucher is null) return (false, error, null);
        voucher.BuyerUserId = buyerUserId;
        db.GiftVouchers.Add(voucher);
        await db.SaveChangesAsync();
        return (true, null, voucher.Id);
    }

    public async Task<(bool Ok, string? Error, GiftVoucherDto? Voucher)> IssueAsync(string issuedByUserId, GiftVoucherRequest request)
    {
        await using var db = dbFactory.CreateDbContext();
        var (voucher, error) = await BuildAsync(db, request, manual: true);
        if (voucher is null) return (false, error, null);
        voucher.IssuedByUserId = issuedByUserId;
        db.GiftVouchers.Add(voucher);
        await db.SaveChangesAsync();
        await ActivateAsync(voucher.Id);
        return (true, null, await GetByCodeAsync(voucher.Code));
    }

    public async Task ActivateAsync(int voucherId)
    {
        await using var db = dbFactory.CreateDbContext();
        await GiftVoucherLedger.ActivateAsync(db, voucherId, clock.UtcNow);
        await db.SaveChangesAsync();
    }

    public async Task<(bool Ok, string Message)> RedeemAsync(string code, int clientId)
    {
        var norm = NormalizeCode(code);
        await using var db = dbFactory.CreateDbContext();
        var voucher = await db.GiftVouchers.FirstOrDefaultAsync(v => v.Code == norm);
        if (voucher is null) return (false, "Nie znaleziono bonu o tym kodzie.");
        if (voucher.Status == GiftVoucherStatus.Redeemed) return (false, "Ten bon został już wykorzystany.");
        if (voucher.Status != GiftVoucherStatus.Active) return (false, "Ten bon nie jest aktywny.");
        if (voucher.ExpiresAt is DateTime exp && exp <= clock.UtcNow) return (false, $"Bon wygasł {clock.ToWallClock(exp):dd.MM.yyyy}.");
        if (voucher.Kind == GiftVoucherKind.Amount)
            return (false, $"To bon na kwotę {voucher.Title.Replace("Bon ", "")} — wpisz kod jako kupon przy zakupie w sklepie.");
        if (voucher.SessionTypeId is not int sessionTypeId || voucher.SessionsCount is not int sessions)
            return (false, "Bon nie ma przypisanego pakietu. Skontaktuj się z trenerem.");

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return (false, "Nie znaleziono profilu klienta.");

        var now = clock.UtcNow;
        var creator = voucher.IssuedByUserId ?? client.TrainerUserId ?? "system";
        db.SessionPackages.Add(new SessionPackage
        {
            ClientId = clientId,
            CreatedByUserId = creator,
            Name = voucher.Title,
            Notes = $"Bon podarunkowy {voucher.Code}" + (string.IsNullOrWhiteSpace(voucher.FromName) ? "" : $" od: {voucher.FromName}"),
            SessionTypeId = sessionTypeId,
            TotalSessions = sessions,
            PricePerSession = sessions > 0 ? Math.Round(voucher.Value / sessions, 2) : voucher.Value,
            IsPaid = true,
            PaidAt = voucher.PaidAt ?? now,
            PaymentReference = $"voucher:{voucher.Code}",
            PurchasedAt = now,
            ExpiresAt = voucher.PackageValidDays is int d ? now.AddDays(d) : null,
            Status = PackageStatus.Active
        });
        voucher.Status = GiftVoucherStatus.Redeemed;
        voucher.RedeemedAt = now;
        voucher.RedeemedByClientId = clientId;
        await db.SaveChangesAsync();
        return (true, $"Bon zrealizowany — masz {sessions} {(sessions == 1 ? "sesję" : sessions is >= 2 and <= 4 ? "sesje" : "sesji")} w pakiecie „{voucher.Title}”.");
    }

    public async Task<bool> MarkRedeemedAsync(int voucherId)
    {
        await using var db = dbFactory.CreateDbContext();
        var v = await db.GiftVouchers.FirstOrDefaultAsync(x => x.Id == voucherId && x.Status == GiftVoucherStatus.Active);
        if (v is null) return false;
        v.Status = GiftVoucherStatus.Redeemed;
        v.RedeemedAt = clock.UtcNow;
        await DeactivateCouponAsync(db, v);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CancelAsync(int voucherId)
    {
        await using var db = dbFactory.CreateDbContext();
        var v = await db.GiftVouchers.FirstOrDefaultAsync(x => x.Id == voucherId
            && (x.Status == GiftVoucherStatus.Active || x.Status == GiftVoucherStatus.Pending));
        if (v is null) return false;
        v.Status = GiftVoucherStatus.Cancelled;
        await DeactivateCouponAsync(db, v);
        await db.SaveChangesAsync();
        return true;
    }

    private static async Task DeactivateCouponAsync(ApplicationDbContext db, GiftVoucher v)
    {
        if (v.CouponId is not int couponId) return;
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == couponId);
        if (coupon is not null) coupon.IsActive = false;
    }

    public async Task<GiftVoucherDto?> GetByCodeAsync(string code)
    {
        var norm = NormalizeCode(code);
        await using var db = dbFactory.CreateDbContext();
        return (await QueryAsync(db, db.GiftVouchers.Where(v => v.Code == norm))).FirstOrDefault();
    }

    public async Task<List<GiftVoucherDto>> GetBoughtByAsync(string buyerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await QueryAsync(db, db.GiftVouchers.Where(v => v.BuyerUserId == buyerUserId && v.Status != GiftVoucherStatus.Pending));
    }

    public async Task<List<GiftVoucherDto>> GetAllAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await QueryAsync(db, db.GiftVouchers.Where(v => v.Status != GiftVoucherStatus.Pending));
    }

    private static async Task<List<GiftVoucherDto>> QueryAsync(ApplicationDbContext db, IQueryable<GiftVoucher> source)
    {
        var rows = await source.AsNoTracking().OrderByDescending(v => v.CreatedAt).ToListAsync();
        var couponIds = rows.Where(r => r.CouponId != null).Select(r => r.CouponId!.Value).ToList();
        var usedCoupons = await db.Coupons.AsNoTracking().Where(c => couponIds.Contains(c.Id) && c.UsedCount > 0).Select(c => c.Id).ToListAsync();
        var buyerIds = rows.Where(r => r.BuyerUserId != null).Select(r => r.BuyerUserId!).Distinct().ToList();
        var buyers = await db.Users.AsNoTracking().Where(u => buyerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);
        var clientIds = rows.Where(r => r.RedeemedByClientId != null).Select(r => r.RedeemedByClientId!.Value).ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName);

        return rows.Select(v => new GiftVoucherDto
        {
            Id = v.Id, Code = v.Code, Kind = v.Kind, Status = v.Status, Value = v.Value, Currency = v.Currency, Title = v.Title,
            SessionsCount = v.SessionsCount, RecipientName = v.RecipientName, FromName = v.FromName, Message = v.Message,
            IssuedManually = v.IssuedManually, BuyerEmail = v.BuyerUserId is null ? null : buyers.GetValueOrDefault(v.BuyerUserId),
            CreatedAt = v.CreatedAt, PaidAt = v.PaidAt, ExpiresAt = v.ExpiresAt, RedeemedAt = v.RedeemedAt,
            RedeemedByName = v.RedeemedByClientId is int cid ? clients.GetValueOrDefault(cid) : null,
            CouponUsed = v.CouponId is int coupon && usedCoupons.Contains(coupon)
        }).ToList();
    }

    public async Task<(byte[] Bytes, string FileName)> GeneratePdfAsync(int voucherId)
    {
        await using var db = dbFactory.CreateDbContext();
        var v = await db.GiftVouchers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == voucherId)
            ?? throw new InvalidOperationException("Bon nie istnieje.");
        if (v.Status == GiftVoucherStatus.Pending) throw new InvalidOperationException("Bon nie jest jeszcze opłacony.");

        var brand = await branding.GetAsync();
        var company = brand.CompanyName ?? "PTScheduler";
        var accent = ThemeColor(brand.ThemeName);
        byte[]? logo = null;
        try
        {
            if (!string.IsNullOrEmpty(brand.LogoPath))
            {
                var abs = Path.Combine(webRoot.WebRootPath, brand.LogoPath.TrimStart('/'));
                if (File.Exists(abs)) logo = await File.ReadAllBytesAsync(abs);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Voucher PDF without logo."); }

        var expires = v.ExpiresAt is DateTime e ? clock.ToWallClock(e).ToString("d MMMM yyyy", Pl) : null;
        var what = v.Kind == GiftVoucherKind.Amount
            ? $"{v.Value.ToString("0.##", Pl)} zł"
            : v.Title;
        var how = v.Kind == GiftVoucherKind.Amount
            ? "Wpisz kod jako kupon przy zakupie pakietu, karnetu lub kursu w sklepie."
            : "Zaloguj się w aplikacji trenera (albo załóż konto), wejdź w „Bony podarunkowe” i wpisz kod w polu „Mam bon”.";

        var pdf = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A5.Landscape());
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontFamily("DejaVu Sans").FontColor(Colors.Grey.Darken4));
                page.Content().Row(row =>
                {
                    row.ConstantItem(18).Background(accent);
                    row.RelativeItem().Padding(28).Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("BON PODARUNKOWY").FontSize(11).Bold().LetterSpacing(0.15f).FontColor(accent);
                                c.Item().Text(company).FontSize(12).FontColor(Colors.Grey.Darken1);
                            });
                            if (logo is not null) r.ConstantItem(70).Height(40).Image(logo).FitArea();
                        });
                        col.Item().PaddingTop(10).Text(what).FontSize(v.Kind == GiftVoucherKind.Amount ? 40 : 26).Bold();
                        if (v.Kind == GiftVoucherKind.Package && v.SessionsCount is int n)
                            col.Item().Text($"{n} {(n == 1 ? "sesja" : n is >= 2 and <= 4 ? "sesje" : "sesji")} treningowych").FontSize(12).FontColor(Colors.Grey.Darken1);
                        if (!string.IsNullOrWhiteSpace(v.RecipientName))
                            col.Item().PaddingTop(6).Text(t => { t.Span("Dla: ").FontColor(Colors.Grey.Darken1); t.Span(v.RecipientName).Bold(); });
                        if (!string.IsNullOrWhiteSpace(v.FromName))
                            col.Item().Text(t => { t.Span("Od: ").FontColor(Colors.Grey.Darken1); t.Span(v.FromName).Bold(); });
                        if (!string.IsNullOrWhiteSpace(v.Message))
                            col.Item().PaddingTop(4).Text($"„{v.Message}”").Italic().FontSize(11);
                        col.Item().PaddingTop(12).Border(1.5f).BorderColor(accent).Padding(10).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("KOD").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                c.Item().Text(v.Code).FontSize(20).Bold().LetterSpacing(0.08f);
                            });
                            if (expires is not null)
                                r.ConstantItem(150).AlignRight().AlignMiddle().Text($"Ważny do {expires}").FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                        col.Item().PaddingTop(4).Text(how).FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }).GeneratePdf();

        return (pdf, $"bon-{v.Code}.pdf");
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length > max ? t[..max] : t;
    }

    private static string ThemeColor(string theme) => theme switch
    {
        "forest" => "#16A34A", "sunset" => "#EA580C", "crimson" => "#DC2626",
        "lavender" => "#9333EA", "slate" => "#475569", "rose" => "#E11D48",
        "teal" => "#0D9488", "amber" => "#D97706", "indigo" => "#4F46E5",
        _ => "#0284C7"
    };
}

/// <summary>
/// Aktywacja bonu w podanym kontekście — używana także przez PaymentService, żeby bon
/// aktywował się w tej samej transakcji co opłacenie zamówienia.
/// </summary>
public static class GiftVoucherLedger
{
    public static async Task ActivateAsync(ApplicationDbContext db, int voucherId, DateTime now)
    {
        var voucher = await db.GiftVouchers.FirstOrDefaultAsync(v => v.Id == voucherId);
        if (voucher is null || voucher.Status != GiftVoucherStatus.Pending) return;

        var months = await db.MarketingSettings.AsNoTracking().Select(m => (int?)m.VoucherValidMonths).FirstOrDefaultAsync() ?? 12;
        voucher.Status = GiftVoucherStatus.Active;
        voucher.PaidAt = now;
        voucher.ExpiresAt = now.AddMonths(months <= 0 ? 12 : Math.Clamp(months, 1, 36));

        if (voucher.Kind == GiftVoucherKind.Amount)
        {
            // Bon kwotowy działa w sklepie jak jednorazowy kupon z tym samym kodem.
            var coupon = new Coupon
            {
                Code = voucher.Code,
                Description = $"Bon podarunkowy {voucher.Title}",
                DiscountType = "amount",
                DiscountValue = voucher.Value,
                ValidFrom = now,
                ValidUntil = voucher.ExpiresAt,
                MaxUses = 1,
                MaxUsesPerUser = 1,
                Scope = "all",
                IsActive = true,
                CreatedAt = now
            };
            db.Coupons.Add(coupon);
            await db.SaveChangesAsync();
            voucher.CouponId = coupon.Id;
        }
    }
}
