using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>Pozycja rachunku przed zapisaniem (podgląd i wystawienie).</summary>
public sealed record BillLineDraft(string Name, string? Detail, int Quantity, decimal UnitPrice, int? OfferItemId = null)
{
    public decimal Amount => UnitPrice * Quantity;
}

/// <summary>Ustawienia automatycznych rachunków (Portal → Rachunki).</summary>
public sealed class BillingSettings
{
    public bool AutoEnabled { get; set; }
    public int BillingDay { get; set; } = 1;
    public int DueDays { get; set; } = 7;
    public bool Reminders { get; set; } = true;
}

/// <summary>
/// Automatyczne rozliczanie trenerów: co miesiąc rachunek z oferty trenera (abonament, dodatki, opłaty
/// i jednorazowe zakupy), e-mail z linkiem do płatności online, przypomnienia i informacja dla administratora
/// o zaległościach. Płatności przez Autopay / PayU / Przelewy24 / Stripe potwierdzają rachunek same.
/// </summary>
public class BillingService(
    IDbContextFactory<PortalDbContext> dbFactory,
    OfferService offers,
    SiteSettingsService settings,
    StorePaymentService payments,
    EmailService email,
    CreditService credits,
    IConfiguration config,
    ILogger<BillingService> logger)
{
    private static readonly CultureInfo Pl = new("pl-PL");
    private static readonly TimeZoneInfo Warsaw = FindWarsaw();

    /// <summary>Pierwsze przypomnienie w dniu terminu, drugie tydzień później, administrator po dwóch tygodniach.</summary>
    public const int SecondReminderAfterDays = 7;
    public const int EscalateAfterDays = 14;

    private static TimeZoneInfo FindWarsaw()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch { return TimeZoneInfo.Local; }
    }

    public static DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Warsaw);
    public static DateOnly Today => DateOnly.FromDateTime(LocalNow);
    public static DateOnly CurrentPeriod => new(Today.Year, Today.Month, 1);
    public static string PeriodLabel(DateOnly period) =>
        Pl.TextInfo.ToTitleCase(Pl.DateTimeFormat.MonthNames[period.Month - 1]) + " " + period.Year;

    private string PublicUrl => (config.GetValue<string>("Portal:PublicUrl") ?? "").TrimEnd('/');
    public string PayUrl(TenantBill bill) => $"{PublicUrl}/rachunek/{bill.PayToken}";

    // ── Ustawienia ──

    public async Task<BillingSettings> GetSettingsAsync()
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.BillingAutoEnabled, SiteSettingsService.Keys.BillingDay,
            SiteSettingsService.Keys.BillingDueDays, SiteSettingsService.Keys.BillingReminders);
        return new BillingSettings
        {
            AutoEnabled = s[SiteSettingsService.Keys.BillingAutoEnabled] == "true",
            BillingDay = int.TryParse(s[SiteSettingsService.Keys.BillingDay], out var d) ? Math.Clamp(d, 1, 28) : 1,
            DueDays = int.TryParse(s[SiteSettingsService.Keys.BillingDueDays], out var due) ? Math.Clamp(due, 1, 60) : 7,
            Reminders = s[SiteSettingsService.Keys.BillingReminders] != "false"
        };
    }

    public Task SaveSettingsAsync(BillingSettings value) => settings.SetManyAsync(new Dictionary<string, string>
    {
        [SiteSettingsService.Keys.BillingAutoEnabled] = value.AutoEnabled ? "true" : "false",
        [SiteSettingsService.Keys.BillingDay] = Math.Clamp(value.BillingDay, 1, 28).ToString(CultureInfo.InvariantCulture),
        [SiteSettingsService.Keys.BillingDueDays] = Math.Clamp(value.DueDays, 1, 60).ToString(CultureInfo.InvariantCulture),
        [SiteSettingsService.Keys.BillingReminders] = value.Reminders ? "true" : "false"
    });

    // ── Pozycje rachunku ──

    /// <summary>Co trafi na rachunek za dany miesiąc (bez zapisywania).</summary>
    public async Task<List<BillLineDraft>> DraftAsync(int tenantId, DateOnly period)
    {
        var offer = await offers.GetAsync(tenantId);
        if (offer is null) return [];
        var tenant = offer.Tenant;
        var paidByCard = !string.IsNullOrWhiteSpace(tenant.StripeSubscriptionId); // abonament pobiera Stripe z karty
        var month = PeriodLabel(period);
        var lines = new List<BillLineDraft>();

        await using var db = dbFactory.CreateDbContext();
        var subscriptionStart = await db.Subscriptions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired)
            .OrderByDescending(s => s.CreatedAt).Select(s => (DateTime?)s.StartsAt).FirstOrDefaultAsync()
            ?? tenant.ProvisionedAt ?? tenant.CreatedAt;
        bool Anniversary(DateTime start) => start.Month == period.Month;

        if (!paidByCard && offer.PlanAmount > 0)
        {
            var planName = offer.Plan?.Name ?? tenant.PlanId;
            if (offer.PlanCycle == OfferBilling.Monthly)
                lines.Add(new BillLineDraft($"Abonament {planName}", month, 1, offer.PlanAmount));
            else if (Anniversary(subscriptionStart))
                lines.Add(new BillLineDraft($"Abonament {planName} (roczny)", $"{period:MM.yyyy} – {period.AddYears(1).AddDays(-1):MM.yyyy}", 1, offer.PlanAmount));
        }

        foreach (var a in offer.Addons.Where(a => !a.ViaCard && a.Total > 0))
            lines.Add(new BillLineDraft(a.Name, $"Dodatek miesięczny · {month}", a.Quantity, a.UnitPrice));

        var from = period.ToDateTime(TimeOnly.MinValue);
        var to = period.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        bool ActiveInPeriod(TenantOfferItem c) => c.CancelledAt is null && c.StartsAt < to && (c.EndsAt is null || c.EndsAt > from);
        foreach (var c in offer.Charges.Where(c => c.Billing == OfferBilling.Monthly && ActiveInPeriod(c)))
            lines.Add(new BillLineDraft(c.Name, $"Co miesiąc · {month}", c.Quantity, c.UnitPrice));
        foreach (var c in offer.Charges.Where(c => c.Billing == OfferBilling.Yearly && ActiveInPeriod(c) && Anniversary(c.StartsAt)))
            lines.Add(new BillLineDraft(c.Name, "Co rok", c.Quantity, c.UnitPrice));
        foreach (var c in offer.OneTimeDue.Where(c => c.Total > 0))
            lines.Add(new BillLineDraft(c.Name, $"Jednorazowo · {c.CreatedAt:dd.MM.yyyy}", c.Quantity, c.UnitPrice, c.Id));

        return lines;
    }

    // ── Wystawianie ──

    public async Task<(bool Ok, string Message, TenantBill? Bill)> IssueAsync(int tenantId, DateOnly period, bool sendEmail = true)
    {
        var cfg = await GetSettingsAsync();
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return (false, "Nie ma takiego trenera.", null);
        if (await db.TenantBills.AnyAsync(b => b.TenantId == tenantId && b.PeriodStart == period && b.Status != TenantBillStatus.Cancelled))
            return (false, $"Rachunek za {PeriodLabel(period).ToLower(Pl)} już jest wystawiony.", null);

        var lines = await DraftAsync(tenantId, period);
        var amount = lines.Sum(l => l.Amount);
        if (amount <= 0) return (false, "Nie ma nic do rozliczenia w tym miesiącu.", null);

        var prefix = $"R/{period:yyyy}/{period:MM}/";
        var count = await db.TenantBills.CountAsync(b => b.Number.StartsWith(prefix));
        var bill = new TenantBill
        {
            TenantId = tenantId,
            Number = prefix + (count + 1).ToString("0000"),
            PeriodStart = period,
            Amount = amount,
            IssuedAt = DateTime.UtcNow,
            DueDate = Today.AddDays(cfg.DueDays),
            PayToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant(),
            Lines = lines.Select(l => new TenantBillLine
            {
                Name = l.Name, Detail = l.Detail, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Amount = l.Amount, OfferItemId = l.OfferItemId
            }).ToList()
        };
        db.TenantBills.Add(bill);

        // Jednorazowe opłaty rozliczone tym rachunkiem — nie trafią na kolejny.
        var oneTimeIds = lines.Where(l => l.OfferItemId is not null).Select(l => l.OfferItemId!.Value).ToList();
        if (oneTimeIds.Count > 0)
            foreach (var item in await db.TenantOfferItems.Where(i => oneTimeIds.Contains(i.Id)).ToListAsync())
                item.SettledAt = bill.IssuedAt;

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenantId, EventType = TenantEventTypes.BillIssued,
            Detail = $"Rachunek {bill.Number} na {amount:0.00} zł"
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Wystawiono rachunek {Number} dla {Slug}: {Amount} zł", bill.Number, tenant.Slug, amount);

        if (sendEmail) await SendBillEmailAsync(bill.Id, BillEmailKind.New);
        return (true, $"Wystawiono rachunek {bill.Number} na {amount.ToString("0.00", Pl)} zł.", bill);
    }

    /// <summary>
    /// Automat (co godzinę): w dniu rozliczenia wystawia brakujące rachunki za bieżący miesiąc,
    /// potem wysyła przypomnienia i informuje administratora o dużych zaległościach.
    /// </summary>
    public async Task<(int Issued, int Reminders)> RunAsync()
    {
        var cfg = await GetSettingsAsync();
        var issued = 0;
        if (cfg.AutoEnabled && Today.Day >= cfg.BillingDay)
        {
            var period = CurrentPeriod;
            foreach (var tenantId in await EligibleTenantIdsAsync(period))
            {
                try
                {
                    var (ok, _, _) = await IssueAsync(tenantId, period);
                    if (ok) issued++;
                }
                catch (Exception ex) { logger.LogError(ex, "Nie udało się wystawić rachunku dla trenera #{Id}.", tenantId); }
            }
        }
        var reminders = cfg.AutoEnabled && cfg.Reminders ? await RemindAsync() : 0;
        return (issued, reminders);
    }

    /// <summary>Aktywni trenerzy po okresie próbnym, bez rachunku za ten miesiąc.</summary>
    public async Task<List<int>> EligibleTenantIdsAsync(DateOnly period)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var billed = db.TenantBills.Where(b => b.PeriodStart == period && b.Status != TenantBillStatus.Cancelled).Select(b => b.TenantId);
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active && (t.TrialEndsAt == null || t.TrialEndsAt <= now) && !billed.Contains(t.Id))
            .Select(t => t.Id).ToListAsync();
    }

    private async Task<int> RemindAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var today = Today;
        var open = await db.TenantBills.Include(b => b.Tenant)
            .Where(b => b.Status == TenantBillStatus.Issued && b.DueDate <= today).ToListAsync();
        var sent = 0;
        foreach (var bill in open)
        {
            var overdueDays = today.DayNumber - bill.DueDate.DayNumber;
            if (bill.ReminderCount == 0 || (bill.ReminderCount == 1 && overdueDays >= SecondReminderAfterDays))
            {
                if (await SendBillEmailAsync(bill.Id, BillEmailKind.Reminder)) sent++;
            }
            else if (overdueDays >= EscalateAfterDays && bill.EscalatedAt is null)
            {
                await NotifyAdminOverdueAsync(bill, overdueDays);
                bill.EscalatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        return sent;
    }

    private async Task NotifyAdminOverdueAsync(TenantBill bill, int overdueDays)
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.AdminNotificationEmail, SiteSettingsService.Keys.AdminNotificationPhone,
            SiteSettingsService.Keys.NotifyTicketsSms);
        var who = bill.Tenant?.CompanyName is { Length: > 0 } c ? c : bill.Tenant?.Slug ?? "trener";
        var link = $"{PublicUrl}/panel/billing";
        var adminEmail = s[SiteSettingsService.Keys.AdminNotificationEmail];
        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var html = Layout("Zaległy rachunek", $"""
                <p>Rachunek <strong>{Enc(bill.Number)}</strong> na <strong>{Money(bill.Amount)}</strong> ({Enc(who)}) nadal nie jest opłacony.</p>
                <p>Termin minął {overdueDays} dni temu, trener dostał już dwa przypomnienia. Zdecyduj, co dalej — np. telefon albo zawieszenie instancji.</p>
                """, "Otwórz rachunki", link);
            await email.SendAsync(adminEmail, $"Zaległy rachunek: {who} ({Money(bill.Amount)})", html);
        }
        var phone = s[SiteSettingsService.Keys.AdminNotificationPhone];
        if (!string.IsNullOrWhiteSpace(phone) && s[SiteSettingsService.Keys.NotifyTicketsSms] == "true")
            await credits.SendPlatformSmsAsync(phone, $"PTScheduler: zalegly rachunek {bill.Number} ({who}, {bill.Amount:0} zl, {overdueDays} dni po terminie). {link}");
    }

    // ── Obsługa rachunków w panelu ──

    public async Task<List<TenantBill>> ListAsync(int? tenantId = null, int take = 300)
    {
        await using var db = dbFactory.CreateDbContext();
        var q = db.TenantBills.AsNoTracking().Include(b => b.Tenant).Include(b => b.Lines).AsQueryable();
        if (tenantId is int id) q = q.Where(b => b.TenantId == id);
        return await q.OrderByDescending(b => b.IssuedAt).Take(take).ToListAsync();
    }

    public async Task<TenantBill?> GetByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return null;
        await using var db = dbFactory.CreateDbContext();
        return await db.TenantBills.AsNoTracking().Include(b => b.Tenant).Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.PayToken == token);
    }

    public async Task<(bool Ok, string Message)> MarkPaidAsync(int billId, string via = "manual", string? note = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var bill = await db.TenantBills.FirstOrDefaultAsync(b => b.Id == billId);
        if (bill is null) return (false, "Nie ma takiego rachunku.");
        if (bill.Status != TenantBillStatus.Issued) return (false, "Ten rachunek nie czeka na płatność.");
        await SettleAsync(db, bill, via, null, note);
        return (true, $"Rachunek {bill.Number} oznaczony jako opłacony.");
    }

    public async Task<(bool Ok, string Message)> CancelAsync(int billId)
    {
        await using var db = dbFactory.CreateDbContext();
        var bill = await db.TenantBills.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == billId);
        if (bill is null) return (false, "Nie ma takiego rachunku.");
        if (bill.Status != TenantBillStatus.Issued) return (false, "Można anulować tylko nieopłacony rachunek.");
        bill.Status = TenantBillStatus.Cancelled;
        // Jednorazowe opłaty wracają do rozliczenia — trafią na kolejny rachunek.
        var ids = bill.Lines.Where(l => l.OfferItemId is not null).Select(l => l.OfferItemId!.Value).ToList();
        foreach (var item in await db.TenantOfferItems.Where(i => ids.Contains(i.Id)).ToListAsync())
            item.SettledAt = null;
        await db.SaveChangesAsync();
        return (true, $"Rachunek {bill.Number} anulowany.");
    }

    public async Task<(bool Ok, string Message)> SendReminderAsync(int billId)
    {
        var ok = await SendBillEmailAsync(billId, BillEmailKind.Reminder);
        return ok ? (true, "Przypomnienie wysłane.") : (false, "Nie udało się wysłać e-maila — sprawdź ustawienia poczty.");
    }

    // ── Płatność online ──

    public async Task<List<string>> GatewaysAsync() => await payments.GetAvailableGatewaysAsync();

    public async Task<(string? Url, string? Error)> StartPaymentAsync(string token, string gateway)
    {
        await using var db = dbFactory.CreateDbContext();
        var bill = await db.TenantBills.Include(b => b.Tenant).FirstOrDefaultAsync(b => b.PayToken == token);
        if (bill is null) return (null, "Nie ma takiego rachunku.");
        if (bill.Status != TenantBillStatus.Issued) return (null, "Ten rachunek jest już rozliczony.");
        if (!(await payments.GetAvailableGatewaysAsync()).Contains(gateway)) return (null, "Ta metoda płatności jest niedostępna.");

        var sessionId = Guid.NewGuid().ToString("N");
        var (url, externalId, error) = await payments.CreatePaymentAsync(gateway, bill.Amount, $"Rachunek {bill.Number} — PTScheduler",
            sessionId, $"{PayUrl(bill)}?paid=1", PublicUrl, bill.Tenant?.OwnerEmail);
        if (error is not null || url is null) return (null, error ?? "Bramka płatności nie zwróciła adresu.");

        bill.PaymentGateway = gateway;
        bill.PaymentSessionId = sessionId;
        bill.PaymentExternalId = externalId;
        await db.SaveChangesAsync();
        return (url, null);
    }

    /// <summary>Potwierdzenie z bramki. Zwraca true, gdy płatność dotyczyła rachunku (wtedy nie szukamy zamówień sklepu).</summary>
    public async Task<bool> TryConfirmAsync(string gateway, string? externalId, string? sessionId = null)
    {
        if (string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(sessionId)) return false;
        await using var db = dbFactory.CreateDbContext();
        var bill = await db.TenantBills.FirstOrDefaultAsync(b => b.PaymentGateway == gateway
            && ((externalId != null && b.PaymentExternalId == externalId) || (sessionId != null && b.PaymentSessionId == sessionId)));
        if (bill is null) return false;
        if (bill.Status == TenantBillStatus.Issued)
            await SettleAsync(db, bill, gateway, bill.PaymentExternalId ?? externalId, null);
        return true;
    }

    public async Task<bool> IsBillPaymentAsync(string gateway, string sessionId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.TenantBills.AnyAsync(b => b.PaymentGateway == gateway && b.PaymentSessionId == sessionId);
    }

    public async Task<string?> PayUrlBySessionAsync(string sessionId)
    {
        await using var db = dbFactory.CreateDbContext();
        var token = await db.TenantBills.AsNoTracking().Where(b => b.PaymentSessionId == sessionId).Select(b => b.PayToken).FirstOrDefaultAsync();
        return token is null ? null : $"{PublicUrl}/rachunek/{token}?paid=1";
    }

    private async Task SettleAsync(PortalDbContext db, TenantBill bill, string via, string? externalId, string? note)
    {
        bill.Status = TenantBillStatus.Paid;
        bill.PaidAt = DateTime.UtcNow;
        bill.PaidVia = via;
        if (!string.IsNullOrWhiteSpace(note)) bill.AdminNote = note;
        db.PaymentRecords.Add(new PaymentRecord
        {
            TenantId = bill.TenantId,
            ExternalPaymentId = externalId,
            Amount = bill.Amount,
            Currency = bill.Currency,
            Status = PaymentRecordStatus.Paid,
            Source = via,
            Description = $"Rachunek {bill.Number}"
        });
        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = bill.TenantId, EventType = TenantEventTypes.BillPaid,
            Detail = $"Rachunek {bill.Number} opłacony ({via})"
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Rachunek {Number} opłacony ({Via}).", bill.Number, via);
        try { await SendBillEmailAsync(bill.Id, BillEmailKind.Paid); }
        catch (Exception ex) { logger.LogWarning(ex, "Potwierdzenie płatności rachunku {Number} nie wyszło.", bill.Number); }
    }

    // ── E-maile ──

    public enum BillEmailKind { New, Reminder, Paid }

    public async Task<bool> SendBillEmailAsync(int billId, BillEmailKind kind)
    {
        await using var db = dbFactory.CreateDbContext();
        var bill = await db.TenantBills.Include(b => b.Tenant).Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == billId);
        if (bill?.Tenant is null || string.IsNullOrWhiteSpace(bill.Tenant.OwnerEmail)) return false;

        var name = bill.Tenant.OwnerName is { Length: > 0 } n ? n.Split(' ')[0] : "";
        var hello = name.Length > 0 ? $"Cześć {Enc(name)}," : "Cześć,";
        var rows = string.Join("", bill.Lines.Select(l =>
            $"""<tr><td style="padding:6px 0;border-bottom:1px solid #eef2f7">{Enc(l.Name)}{(l.Quantity > 1 ? $" × {l.Quantity}" : "")}<div style="color:#64748b;font-size:12px">{Enc(l.Detail ?? "")}</div></td><td style="padding:6px 0;border-bottom:1px solid #eef2f7;text-align:right;white-space:nowrap">{Money(l.Amount)}</td></tr>"""));
        var table = $"""
            <table style="width:100%;border-collapse:collapse;font-size:14px;margin:16px 0">{rows}
            <tr><td style="padding:10px 0;font-weight:700">Razem</td><td style="padding:10px 0;text-align:right;font-weight:700;font-size:16px">{Money(bill.Amount)}</td></tr></table>
            """;
        var overdue = Today > bill.DueDate;

        var (subject, html) = kind switch
        {
            BillEmailKind.New => ($"Rachunek {bill.Number} za {PeriodLabel(bill.PeriodStart).ToLower(Pl)} — {Money(bill.Amount)}",
                Layout($"Rachunek za {PeriodLabel(bill.PeriodStart).ToLower(Pl)}", $"""
                    <p>{hello}</p>
                    <p>przygotowaliśmy rachunek za PTScheduler. Termin płatności: <strong>{bill.DueDate:dd.MM.yyyy}</strong>.</p>
                    {table}
                    <p style="color:#64748b;font-size:13px">Zapłacisz online (BLIK, karta, przelew) jednym kliknięciem.</p>
                    """, "Zapłać online", PayUrl(bill))),
            BillEmailKind.Reminder => ($"{(overdue ? "Przypomnienie: minął termin płatności" : "Przypomnienie o płatności")} — rachunek {bill.Number}",
                Layout("Przypomnienie o płatności", $"""
                    <p>{hello}</p>
                    <p>rachunek <strong>{Enc(bill.Number)}</strong> na <strong>{Money(bill.Amount)}</strong> {(overdue ? $"miał termin płatności {bill.DueDate:dd.MM.yyyy}" : $"ma termin płatności {bill.DueDate:dd.MM.yyyy}")} i nadal czeka na wpłatę.</p>
                    <p>Jeśli już zapłacono — dziękujemy, ta wiadomość jest nieaktualna.</p>
                    """, "Zapłać teraz", PayUrl(bill))),
            _ => ($"Dziękujemy za płatność — rachunek {bill.Number}",
                Layout("Płatność otrzymana", $"""
                    <p>{hello}</p>
                    <p>potwierdzamy wpłatę <strong>{Money(bill.Amount)}</strong> za rachunek <strong>{Enc(bill.Number)}</strong>. Dziękujemy!</p>
                    {table}
                    """, "Zobacz rachunek", PayUrl(bill)))
        };

        var (ok, error) = await email.SendAsync(bill.Tenant.OwnerEmail, subject, html, toName: bill.Tenant.OwnerName);
        if (!ok)
        {
            logger.LogWarning("E-mail z rachunkiem {Number} nie wyszedł: {Error}", bill.Number, error);
            return false;
        }
        if (kind == BillEmailKind.New) bill.EmailSentAt = DateTime.UtcNow;
        if (kind == BillEmailKind.Reminder)
        {
            bill.ReminderCount++;
            bill.LastReminderAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return true;
    }

    private static string Money(decimal v) => v.ToString("#,0.00", Pl) + " zł";
    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static string Layout(string title, string body, string button, string url) => $"""
        <!DOCTYPE html>
        <html lang="pl"><body style="margin:0;background:#f1f5f9;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,sans-serif;color:#0f172a">
        <div style="max-width:560px;margin:0 auto;padding:24px 16px">
          <div style="background:#fff;border-radius:16px;padding:28px">
            <div style="font-weight:700;color:#7C3AED;margin-bottom:12px">PTScheduler</div>
            <h1 style="font-size:20px;margin:0 0 12px">{Enc(title)}</h1>
            <div style="font-size:15px;line-height:1.6">{body}</div>
            <p style="text-align:center;margin:24px 0 8px"><a href="{Enc(url)}" style="display:inline-block;background:#7C3AED;color:#fff;text-decoration:none;padding:12px 24px;border-radius:10px;font-weight:600">{Enc(button)}</a></p>
          </div>
          <p style="text-align:center;color:#94a3b8;font-size:12px;margin-top:16px">PTScheduler · wiadomość wysłana automatycznie</p>
        </div></body></html>
        """;
}

/// <summary>Co godzinę: automatyczne rachunki i przypomnienia (gdy włączone w Portalu).</summary>
public class BillingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BillingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wysyłamy w ciągu dnia (8–20), żeby e-maile nie przychodziły w nocy.
            var hour = BillingService.LocalNow.Hour;
            if (hour is >= 8 and < 20)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var billing = scope.ServiceProvider.GetRequiredService<BillingService>();
                    var (issued, reminders) = await billing.RunAsync();
                    if (issued + reminders > 0)
                        logger.LogInformation("Rachunki: wystawiono {Issued}, przypomnienia {Reminders}.", issued, reminders);
                }
                catch (Exception ex) { logger.LogError(ex, "Automatyczne rachunki nie powiodły się."); }
            }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
