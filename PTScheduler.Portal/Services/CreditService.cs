using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public class CreditService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    ILogger<CreditService> logger)
{
    /// <summary>Ustawiane przez <see cref="FulfillOrderAsync"/>, gdy zamówienie aktywowało dodatek (trzeba odświeżyć uprawnienia instancji).</summary>
    public bool AddonActivated { get; private set; }

    public async Task<Dictionary<string, decimal>> GetBalancesAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.TenantCredits
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .ToDictionaryAsync(c => c.CreditType, c => c.Balance);
    }

    public async Task AddCreditsAsync(int tenantId, string creditType, decimal amount, string? description = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        // Atomowo, jak odejmowanie — doładowanie nie nadpisze równoległej wysyłki.
        var updated = await db.TenantCredits
            .Where(c => c.TenantId == tenantId && c.CreditType == creditType)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.Balance, c => c.Balance + amount)
                .SetProperty(c => c.TotalPurchased, c => c.TotalPurchased + amount)
                .SetProperty(c => c.UpdatedAt, now));
        if (updated == 0)
        {
            db.TenantCredits.Add(new TenantCredit
            {
                TenantId = tenantId,
                CreditType = creditType,
                Balance = amount,
                TotalPurchased = amount,
                UpdatedAt = now
            });
        }

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenantId,
            EventType = TenantEventTypes.CreditAdded,
            Detail = $"{creditType}: +{amount} ({description ?? "zakup"})"
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Credits added: tenant={TenantId} type={Type} amount={Amount}", tenantId, creditType, amount);
    }

    public async Task<(bool Success, decimal Remaining)> DeductCreditAsync(int tenantId, string creditType, decimal amount)
    {
        await using var db = dbFactory.CreateDbContext();
        // Jedno warunkowe UPDATE w bazie: równoległe wysyłki tej samej instancji nie zejdą
        // poniżej zera (odczyt-zmiana-zapis gubił odejmowania przy jednoczesnych SMS-ach).
        var now = DateTime.UtcNow;
        var updated = await db.TenantCredits
            .Where(c => c.TenantId == tenantId && c.CreditType == creditType && c.Balance >= amount)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.Balance, c => c.Balance - amount)
                .SetProperty(c => c.TotalUsed, c => c.TotalUsed + amount)
                .SetProperty(c => c.UpdatedAt, now));

        var balance = await db.TenantCredits.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CreditType == creditType)
            .Select(c => (decimal?)c.Balance)
            .FirstOrDefaultAsync() ?? 0;
        return (updated == 1, balance);
    }

    public async Task FulfillOrderAsync(ServiceOrder order, ServiceItem item)
    {
        if (item.FulfillmentType == "manual" || item.CreditAmount <= 0) return;

        // Dodatek miesięczny (np. +100 GB transferu) opłacony w sklepie — podnosi limit, zamiast dodawać kredyty.
        if (AddonService.IsMonthlyAddon(item))
        {
            await using var adb = dbFactory.CreateDbContext();
            AddonService.ActivateFromOrder(adb, order.TenantId, item, order.Id);
            var o = await adb.ServiceOrders.FindAsync(order.Id);
            if (o is not null)
            {
                o.Status = ServiceOrderStatus.Completed;
                o.CompletedAt = DateTime.UtcNow;
                o.AdminNotes = "Dodatek miesięczny aktywny — kolejne miesiące rozliczaj ręcznie albo zrezygnuj w karcie trenera.";
            }
            await adb.SaveChangesAsync();
            AddonActivated = true;
            return;
        }

        var creditType = item.FulfillmentType switch
        {
            "credit_sms" => "sms",
            "credit_cdn_storage" => "cdn_storage_gb",
            "credit_cdn_bandwidth" => "cdn_bandwidth_gb",
            _ => null
        };

        if (creditType is null) return;

        await AddCreditsAsync(order.TenantId, creditType, item.CreditAmount,
            $"Zamówienie #{order.Id}: {item.Name}");

        await using var db = dbFactory.CreateDbContext();
        var dbOrder = await db.ServiceOrders.FindAsync(order.Id);
        if (dbOrder is not null)
        {
            dbOrder.Status = ServiceOrderStatus.Completed;
            dbOrder.CompletedAt = DateTime.UtcNow;
            dbOrder.AdminNotes = "Auto-fulfilled: kredyty dodane automatycznie";
            await db.SaveChangesAsync();
        }
    }

    public static int MonthKey(DateTime utc) => utc.Year * 100 + utc.Month;

    /// <summary>
    /// Stan SMS trenera: miesięczny limit (plan + oferta), ile z niego wysłano w tym miesiącu
    /// i ile ma kredytów (dokupionych albo w gratisie — nie wygasają).
    /// </summary>
    public async Task<SmsStatus> GetSmsStatusAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == tenantId);
        var extra = await AddonService.ExtraLimitsAsync(db, tenantId);
        var planLimit = tenant?.Plan is { SmsReminders: true } plan ? plan.MaxSmsPerMonth : 0;
        var limit = planLimit >= int.MaxValue - extra.SmsPerMonth ? int.MaxValue : planLimit + extra.SmsPerMonth;
        var month = MonthKey(DateTime.UtcNow);
        var used = await db.TenantSmsCounters.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Month == month).Select(c => c.Count).FirstOrDefaultAsync();
        var credits = await db.TenantCredits.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.CreditType == "sms").Select(c => c.Balance).FirstOrDefaultAsync();
        var enabled = !string.IsNullOrWhiteSpace(await settings.GetAsync(SiteSettingsService.Keys.PlatformSmsApiToken));
        return new SmsStatus(enabled, limit, used, credits);
    }

    /// <summary>
    /// Wysyłka przez konto SMSAPI platformy. Najpierw z miesięcznego limitu planu (i oferty),
    /// potem z kredytów. Nieudana wysyłka oddaje SMS-a do limitu albo kredytów.
    /// </summary>
    public async Task<SmsSendResult> SendSmsCentralizedAsync(int tenantId, string phone, string message)
    {
        var token = await settings.GetAsync(SiteSettingsService.Keys.PlatformSmsApiToken);
        if (string.IsNullOrWhiteSpace(token))
            return new(false, "SMS nie są jeszcze uruchomione na platformie.", false);

        var status = await GetSmsStatusAsync(tenantId);
        var month = MonthKey(DateTime.UtcNow);
        var fromAllowance = status.MonthlyLimit > 0 && await TryUseAllowanceAsync(tenantId, month, status.MonthlyLimit);
        if (!fromAllowance)
        {
            var (ok, _) = await DeductCreditAsync(tenantId, "sms", 1);
            if (!ok)
                return new(false, status.MonthlyLimit > 0
                    ? $"Wykorzystano miesięczny limit SMS ({status.MonthlyLimit}). Dodaj SMS-y do abonamentu w sklepie."
                    : "Brak SMS-ów do wysłania — dodaj SMS-y do abonamentu w sklepie.", true);
        }

        var senderName = await settings.GetAsync(SiteSettingsService.Keys.PlatformSmsSenderName);
        var (sent, error) = await SendSmsApiAsync(token, senderName, phone, message);
        if (!sent)
        {
            await using var db = dbFactory.CreateDbContext();
            if (fromAllowance)
                await db.TenantSmsCounters.Where(c => c.TenantId == tenantId && c.Month == month && c.Count > 0)
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.Count, c => c.Count - 1));
            else
                await db.TenantCredits.Where(c => c.TenantId == tenantId && c.CreditType == "sms")
                    .ExecuteUpdateAsync(u => u.SetProperty(c => c.Balance, c => c.Balance + 1).SetProperty(c => c.TotalUsed, c => c.TotalUsed - 1));
            return new(false, error, false);
        }
        return new(true, null, false);
    }

    /// <summary>Atomowo zwiększa licznik miesiąca, o ile nie przekroczy limitu.</summary>
    private async Task<bool> TryUseAllowanceAsync(int tenantId, int month, int limit)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = dbFactory.CreateDbContext();
            var updated = await db.TenantSmsCounters
                .Where(c => c.TenantId == tenantId && c.Month == month && c.Count < limit)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Count, c => c.Count + 1));
            if (updated == 1) return true;
            if (await db.TenantSmsCounters.AnyAsync(c => c.TenantId == tenantId && c.Month == month)) return false;
            try
            {
                db.TenantSmsCounters.Add(new TenantSmsCounter { TenantId = tenantId, Month = month, Count = 1 });
                await db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException) { /* równoległy pierwszy SMS miesiąca — ponów przez UPDATE */ }
        }
        return false;
    }

    /// <summary>SMS od platformy (np. do administratora) — z konta SMSAPI platformy, bez limitu trenera.</summary>
    public async Task<(bool Success, string? Error)> SendPlatformSmsAsync(string phone, string message)
    {
        var token = await settings.GetAsync(SiteSettingsService.Keys.PlatformSmsApiToken);
        if (string.IsNullOrWhiteSpace(token)) return (false, "SMS nie są skonfigurowane na platformie.");
        var senderName = await settings.GetAsync(SiteSettingsService.Keys.PlatformSmsSenderName);
        return await SendSmsApiAsync(token, senderName, phone, message);
    }

    private async Task<(bool Success, string? Error)> SendSmsApiAsync(
        string apiToken, string senderName, string phone, string message)
    {
        var normalized = NormalizePhone(phone);
        if (normalized is null) return (false, "Nieprawidłowy numer telefonu.");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.smsapi.pl/sms.do");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            var fields = new Dictionary<string, string>
            {
                ["to"] = normalized,
                ["message"] = message,
                ["format"] = "json"
            };
            if (!string.IsNullOrWhiteSpace(senderName))
                fields["from"] = senderName;
            req.Content = new FormUrlEncodedContent(fields);

            var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out _))
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "Błąd SMSAPI";
                return (false, msg);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Platform SMSAPI.pl send failed");
            return (false, "Błąd połączenia z SMSAPI.pl.");
        }
    }

    private static string? NormalizePhone(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            9 => "48" + digits,
            11 when digits.StartsWith("48") => digits,
            _ when digits.Length > 9 => digits,
            _ => null
        };
    }
}

/// <summary>Stan SMS trenera w Portalu.</summary>
public sealed record SmsStatus(bool PlatformEnabled, int MonthlyLimit, int MonthlyUsed, decimal Credits)
{
    public bool Unlimited => MonthlyLimit == int.MaxValue;
    public int MonthlyLeft => Unlimited ? int.MaxValue : Math.Max(0, MonthlyLimit - MonthlyUsed);
    public bool CanSend => PlatformEnabled && (MonthlyLeft > 0 || Credits >= 1);
}

public sealed record SmsSendResult(bool Success, string? Error, bool QuotaExceeded);
