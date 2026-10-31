using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Ksef;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services.Ksef;

public class KsefService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    IInvoiceService invoices,
    IBrandingService branding,
    IAppClock clock,
    KsefClient client,
    ILogger<KsefService> logger) : IKsefService
{
    private IDataProtector Protector => dataProtection.CreateProtector("PTScheduler.Ksef.Token.v1");

    public async Task SaveTokenAsync(string? token)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await GetOrCreateConfigAsync(db);
        cfg.KsefTokenProtected = string.IsNullOrWhiteSpace(token) ? null : Protector.Protect(token.Trim());
        await db.SaveChangesAsync();
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.FinanceTaxConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Module == "standard");
        var (ready, error, token) = Ready(cfg);
        if (!ready) return (false, error!);
        try
        {
            await client.TestAsync(BaseUrl(cfg!), cfg!.SellerNip!, token!);
            return (true, $"Połączono z KSeF ({EnvLabel(cfg.KsefEnvironment)}) — token jest poprawny.");
        }
        catch (Exception ex) when (ex is KsefException or HttpRequestException or TaskCanceledException)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string? Error)> SetBuyerAsync(int orderId, InvoiceBuyerDto buyer)
    {
        var nip = Nip.Normalize(buyer.Nip);
        if (nip.Length > 0 && !Nip.IsValid(nip)) return (false, "Nieprawidłowy NIP nabywcy.");
        if (nip.Length > 0 && string.IsNullOrWhiteSpace(buyer.Name)) return (false, "Podaj nazwę firmy nabywcy.");

        await using var db = dbFactory.CreateDbContext();
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return (false, "Nie znaleziono zamówienia.");
        if (order.KsefStatus is KsefStatus.Sent or KsefStatus.Accepted)
            return (false, "Faktura jest już w KSeF — zmiana nabywcy wymaga faktury korygującej.");

        order.BuyerNip = nip.Length > 0 ? nip : null;
        order.BuyerName = Clean(buyer.Name);
        order.BuyerAddress = Clean(buyer.Address);
        order.BuyerPostalCode = Clean(buyer.PostalCode);
        order.BuyerCity = Clean(buyer.City);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string Message)> SendOrderInvoiceAsync(int orderId)
    {
        await using (var check = dbFactory.CreateDbContext())
        {
            var o = await check.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId);
            if (o is null) return (false, "Nie znaleziono zamówienia.");
            if (o.Status != OrderStatus.Paid) return (false, "Do KSeF można wysłać tylko fakturę opłaconego zamówienia.");
            if (o.KsefStatus is KsefStatus.Sent or KsefStatus.Accepted) return (false, "Ta faktura jest już w KSeF.");
            // Nadanie numeru faktury (i sprawdzenie numeracji) — ta sama ścieżka co PDF.
            if (string.IsNullOrEmpty(o.InvoiceNumber))
            {
                try { await invoices.GenerateInvoiceAsync(orderId); }
                catch (InvalidOperationException ex) { return (false, ex.Message); }
            }
        }

        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.FinanceTaxConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Module == "standard");
        var (ready, error, token) = Ready(cfg);
        if (!ready) return (false, error!);

        var order = await db.Orders
            .Include(x => x.PackageOffer)
            .Include(x => x.Course)
            .FirstAsync(x => x.Id == orderId);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.ApplicationUserId);

        string xml;
        try
        {
            xml = Fa3XmlBuilder.Build(await BuildInvoiceAsync(cfg!, order, user), clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            return (false, ex.Message);
        }

        try
        {
            var result = await client.SendInvoiceAsync(BaseUrl(cfg!), cfg!.SellerNip!, token!, xml);
            order.KsefStatus = KsefStatus.Sent;
            order.KsefSessionReference = result.SessionReference;
            order.KsefInvoiceReference = result.InvoiceReference;
            order.KsefError = null;
            order.KsefSentAt = clock.UtcNow;
            await db.SaveChangesAsync();
            return (true, "Faktura wysłana do KSeF — numer KSeF pojawi się po przetworzeniu (zwykle w ciągu kilku minut).");
        }
        catch (Exception ex) when (ex is KsefException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "KSeF send failed for order {OrderId}", orderId);
            order.KsefStatus = KsefStatus.Rejected;
            order.KsefError = Truncate(ex.Message, 1000);
            await db.SaveChangesAsync();
            return (false, "KSeF odrzucił wysyłkę: " + ex.Message);
        }
    }

    public async Task<int> RefreshPendingAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.FinanceTaxConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Module == "standard", ct);
        var (ready, _, token) = Ready(cfg);
        if (!ready) return 0;

        var pending = await db.Orders
            .Where(o => o.KsefStatus == KsefStatus.Sent && o.KsefSessionReference != null && o.KsefInvoiceReference != null)
            .OrderBy(o => o.KsefSentAt)
            .Take(20)
            .ToListAsync(ct);

        var changed = 0;
        foreach (var o in pending)
        {
            try
            {
                var state = await client.GetInvoiceStateAsync(BaseUrl(cfg!), cfg!.SellerNip!, token!,
                    o.KsefSessionReference!, o.KsefInvoiceReference!, ct);
                if (!state.Processed) continue;
                o.KsefStatus = state.Accepted ? KsefStatus.Accepted : KsefStatus.Rejected;
                o.KsefNumber = state.KsefNumber;
                o.KsefError = state.Accepted ? null : Truncate(state.Error ?? "Faktura odrzucona przez KSeF.", 1000);
                changed++;
            }
            catch (Exception ex) when (ex is KsefException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "KSeF status check failed for order {OrderId}", o.Id);
            }
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    // ── pomocnicze ─────────────────────────────────────────────────────────

    private async Task<Fa3Invoice> BuildInvoiceAsync(FinanceTaxConfig cfg, Order order, ApplicationUser? user)
    {
        var sellerName = cfg.SellerName;
        if (string.IsNullOrWhiteSpace(sellerName))
        {
            try { sellerName = (await branding.GetAsync()).CompanyName; } catch { /* brak brandingu */ }
        }
        var seller = new Fa3Party(cfg.SellerNip, sellerName ?? "Sprzedawca",
            cfg.SellerAddress, JoinCity(cfg.SellerPostalCode, cfg.SellerCity));

        var consumerName = user is null ? "Klient" : $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(consumerName)) consumerName = user?.Email ?? "Klient";
        var buyer = new Fa3Party(order.BuyerNip, order.BuyerName ?? consumerName,
            order.BuyerAddress, JoinCity(order.BuyerPostalCode, order.BuyerCity));

        var item = order.Kind == OrderKind.Package
            ? order.PackageOffer?.Name ?? "Pakiet treningów"
            : order.Course?.Title ?? "Kurs online";

        var issue = DateOnly.FromDateTime(clock.ToWallClock(order.InvoiceIssuedAt ?? clock.UtcNow));
        var paid = order.PaidAt is { } p ? DateOnly.FromDateTime(clock.ToWallClock(p)) : (DateOnly?)null;
        return new Fa3Invoice(order.InvoiceNumber!, issue, paid ?? issue, seller, buyer,
            [new Fa3Line(item, 1, order.Amount)], cfg.VatEnabled, cfg.VatRate, cfg.VatExemptBasis,
            order.Currency, paid);
    }

    private (bool Ready, string? Error, string? Token) Ready(FinanceTaxConfig? cfg)
    {
        if (cfg is null || !cfg.KsefEnabled) return (false, "Integracja z KSeF jest wyłączona (Finanse → Ustawienia).", null);
        if (!Nip.IsValid(cfg.SellerNip)) return (false, "Uzupełnij poprawny NIP sprzedawcy w ustawieniach.", null);
        if (string.IsNullOrEmpty(cfg.KsefTokenProtected)) return (false, "Wklej token KSeF w ustawieniach.", null);
        try { return (true, null, Protector.Unprotect(cfg.KsefTokenProtected)); }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return (false, "Nie udało się odczytać tokenu KSeF — wklej go ponownie.", null);
        }
    }

    private static string BaseUrl(FinanceTaxConfig cfg) =>
        string.IsNullOrWhiteSpace(cfg.KsefApiUrl) ? KsefClient.DefaultBaseUrl(cfg.KsefEnvironment) : cfg.KsefApiUrl!;

    private static string EnvLabel(string env) => env switch
    {
        "production" => "produkcja",
        "demo" => "środowisko przedprodukcyjne",
        _ => "środowisko testowe"
    };

    private static async Task<FinanceTaxConfig> GetOrCreateConfigAsync(ApplicationDbContext db)
    {
        var cfg = await db.FinanceTaxConfigs.FirstOrDefaultAsync(c => c.Module == "standard");
        if (cfg is null)
        {
            cfg = new FinanceTaxConfig { Module = "standard" };
            db.FinanceTaxConfigs.Add(cfg);
        }
        return cfg;
    }

    private static string? JoinCity(string? postal, string? city) =>
        string.IsNullOrWhiteSpace(city) ? null : string.IsNullOrWhiteSpace(postal) ? city : $"{postal} {city}";

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
