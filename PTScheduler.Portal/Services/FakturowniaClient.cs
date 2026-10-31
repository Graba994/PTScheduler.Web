using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Połączenie z Fakturownia.pl (API v1): wystawienie dokumentu, korekta, PDF i test połączenia.
/// Token API jest w ustawieniach zaszyfrowany (Data Protection) i nie wychodzi nigdzie poza zapytania do Fakturowni.
/// </summary>
public partial class FakturowniaClient(
    IHttpClientFactory httpFactory,
    SiteSettingsService settings,
    IDataProtectionProvider dataProtection,
    ILogger<FakturowniaClient> logger)
{
    public const string HttpClientName = "fakturownia";
    private IDataProtector Secrets => dataProtection.CreateProtector("fakturownia-api-token");

    public sealed record Created(string Id, string Number, string? Url);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex DomainRx();

    /// <summary>„hantly”, „hantly.fakturownia.pl” albo pełny adres → „hantly” (null — niepoprawna).</summary>
    public static string? NormalizeDomain(string? raw)
    {
        var d = (raw ?? "").Trim().ToLowerInvariant();
        d = d.Replace("https://", "").Replace("http://", "");
        d = d.Split('/')[0];
        if (d.EndsWith(".fakturownia.pl")) d = d[..^".fakturownia.pl".Length];
        return DomainRx().IsMatch(d) ? d : null;
    }

    public async Task<string?> GetTokenAsync()
    {
        var raw = await settings.GetAsync(K.FinanceFakturowniaToken);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); }
        catch (CryptographicException)
        {
            logger.LogWarning("Nie da się odszyfrować tokenu Fakturowni — wpisz go ponownie w ustawieniach księgowości.");
            return null;
        }
    }

    public Task SetTokenAsync(string? token) =>
        settings.SetAsync(K.FinanceFakturowniaToken, string.IsNullOrWhiteSpace(token) ? "" : Secrets.Protect(token.Trim()));

    private async Task<(string Base, string Token)?> ConfigAsync()
    {
        var domain = NormalizeDomain(await settings.GetAsync(K.FinanceFakturowniaDomain));
        var token = await GetTokenAsync();
        return domain is null || string.IsNullOrEmpty(token) ? null : ($"https://{domain}.fakturownia.pl", token);
    }

    public async Task<(bool Ok, string Message)> TestAsync(CancellationToken ct = default)
    {
        if (await ConfigAsync() is not { } c) return (false, "Wpisz subdomenę konta i token API.");
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var res = await http.GetAsync($"{c.Base}/invoices.json?per_page=1&page=1&api_token={Uri.EscapeDataString(c.Token)}", ct);
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return (false, "Fakturownia odrzuciła token — sprawdź go w Ustawienia → Ustawienia konta → Integracja.");
            if (res.StatusCode == HttpStatusCode.NotFound) return (false, "Nie ma takiego konta — sprawdź subdomenę (część adresu przed .fakturownia.pl).");
            if (!res.IsSuccessStatusCode) return (false, $"Fakturownia odpowiedziała kodem {(int)res.StatusCode}.");
            return (true, "Połączenie działa.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "Brak połączenia z Fakturownią: " + ex.Message);
        }
    }

    /// <summary>Wystawia dokument w Fakturowni. Zwraca numer nadany przez Fakturownię albo błąd (nigdy wyjątek).</summary>
    public async Task<(Created? Doc, string? Error)> CreateAsync(SalesDocument doc, SalesDocument? original, CancellationToken ct = default)
    {
        if (await ConfigAsync() is not { } c) return (null, "Fakturownia nie jest skonfigurowana (subdomena i token).");
        if (doc.Kind == SalesDocumentKinds.Correction && original?.ExternalId is null)
            return (null, "Korygowany dokument nie jest w Fakturowni — wystaw korektę w Hantly.");
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            var body = JsonSerializer.Serialize(new Dictionary<string, object?> { ["api_token"] = c.Token, ["invoice"] = Payload(doc, original) });
            using var res = await http.PostAsync($"{c.Base}/invoices.json",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return (null, $"Fakturownia: {(int)res.StatusCode} {Shorten(text)}");
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            var id = root.TryGetProperty("id", out var i) ? i.ToString() : null;
            var number = root.TryGetProperty("number", out var n) ? n.GetString() : null;
            var url = root.TryGetProperty("view_url", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(number)) return (null, "Fakturownia nie zwróciła numeru dokumentu.");
            return (new Created(id, number, url), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, "Brak połączenia z Fakturownią: " + ex.Message);
        }
    }

    public async Task<byte[]?> PdfAsync(string externalId, CancellationToken ct = default)
    {
        if (await ConfigAsync() is not { } c || !long.TryParse(externalId, out _)) return null;
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var res = await http.GetAsync($"{c.Base}/invoices/{externalId}.pdf?api_token={Uri.EscapeDataString(c.Token)}", ct);
            if (!res.IsSuccessStatusCode) return null;
            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' ? bytes : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "PDF z Fakturowni ({Id}) niedostępny.", externalId);
            return null;
        }
    }

    /// <summary>Usuwa dokument w Fakturowni. Null — usunięty (albo już go tam nie ma), inaczej powód odmowy.</summary>
    public async Task<string?> DeleteAsync(string externalId, CancellationToken ct = default)
    {
        if (await ConfigAsync() is not { } c) return "Fakturownia nie jest skonfigurowana (subdomena i token).";
        if (!long.TryParse(externalId, out _)) return "Niepoprawny numer dokumentu w Fakturowni.";
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var res = await http.DeleteAsync($"{c.Base}/invoices/{externalId}.json?api_token={Uri.EscapeDataString(c.Token)}", ct);
            if (res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NotFound) return null;
            var text = await res.Content.ReadAsStringAsync(ct);
            return $"Fakturownia: {(int)res.StatusCode} {Shorten(text)}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Brak połączenia z Fakturownią: " + ex.Message;
        }
    }

    public sealed record RemoteLine(string Name, decimal Quantity, string Unit, decimal Gross, decimal Net, string Tax);

    /// <summary>Dokument z Fakturowni (lista /invoices.json z pozycjami).</summary>
    public sealed record RemoteDoc(
        string Id, string Number, string Kind, DateOnly IssueDate, DateOnly SaleDate, DateOnly? PaidDate, string Status,
        string BuyerName, string? BuyerTaxId, string? BuyerAddress, string? BuyerEmail, string Seller,
        decimal Net, decimal Vat, decimal Gross, string Currency, string? PaymentType, string? ViewUrl, string? Oid,
        string? ExemptNote, string? Description, string? CorrectsExternalId, List<RemoteLine> Lines);

    /// <summary>Rodzaje dokumentów, które pobieramy (bez proform, zaliczek i ofert).</summary>
    public static readonly string[] ImportKinds = ["vat", "bill", "correction"];

    /// <summary>Dokumenty z Fakturowni wystawione w okresie (najwyżej 2000). Błąd — null i powód.</summary>
    public async Task<(List<RemoteDoc>? Docs, string? Error)> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (await ConfigAsync() is not { } c) return (null, "Fakturownia nie jest skonfigurowana (subdomena i token).");
        var list = new List<RemoteDoc>();
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            for (var page = 1; page <= 20; page++)
            {
                var url = $"{c.Base}/invoices.json?period=more&date_from={from:yyyy-MM-dd}&date_to={to:yyyy-MM-dd}&page={page}&per_page=100&include_positions=true&api_token={Uri.EscapeDataString(c.Token)}";
                using var res = await http.GetAsync(url, ct);
                var text = await res.Content.ReadAsStringAsync(ct);
                if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return (null, "Fakturownia odrzuciła token.");
                if (!res.IsSuccessStatusCode) return (null, $"Fakturownia: {(int)res.StatusCode} {Shorten(text)}");
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.ValueKind != JsonValueKind.Array) return (null, "Fakturownia zwróciła nieoczekiwaną odpowiedź.");
                var count = 0;
                foreach (var e in json.RootElement.EnumerateArray())
                {
                    count++;
                    if (Parse(e) is { } doc && ImportKinds.Contains(doc.Kind)) list.Add(doc);
                }
                if (count < 100) break;
            }
            return (list, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, "Brak połączenia z Fakturownią: " + ex.Message);
        }
    }

    public static RemoteDoc? Parse(JsonElement e)
    {
        static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;
        static decimal Dec(JsonElement e, string name) =>
            decimal.TryParse(S(e, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;
        static DateOnly? Date(JsonElement e, string name) =>
            DateOnly.TryParseExact(S(e, name) ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        var id = S(e, "id");
        var number = S(e, "number");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(number) || Date(e, "issue_date") is not { } issue) return null;
        var lines = new List<RemoteLine>();
        if (e.TryGetProperty("positions", out var pos) && pos.ValueKind == JsonValueKind.Array)
            foreach (var p in pos.EnumerateArray())
            {
                var kind = S(p, "kind");
                if (kind is "correction_before" or "correction_after") continue;
                lines.Add(new RemoteLine(S(p, "name") ?? "Pozycja", Dec(p, "quantity") is var q && q != 0 ? q : 1, S(p, "quantity_unit") ?? "szt.",
                    Dec(p, "total_price_gross"), Dec(p, "total_price_net"), S(p, "tax") ?? "zw"));
            }
        string? Join(params string?[] parts) => parts.Any(x => !string.IsNullOrWhiteSpace(x)) ? string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x))) : null;
        var buyerCity = string.Join(" ", new[] { S(e, "buyer_post_code"), S(e, "buyer_city") }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var sellerCity = string.Join(" ", new[] { S(e, "seller_post_code"), S(e, "seller_city") }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var sellerTax = S(e, "seller_tax_no");
        var gross = Dec(e, "price_gross");
        var net = e.TryGetProperty("price_net", out _) ? Dec(e, "price_net") : gross;
        var paid = Date(e, "paid_date") ?? (S(e, "status") == "paid" ? Date(e, "sell_date") ?? issue : null);
        return new RemoteDoc(id, number, S(e, "kind") ?? "vat", issue, Date(e, "sell_date") ?? issue, paid, S(e, "status") ?? "",
            S(e, "buyer_name") ?? "Nabywca", string.IsNullOrWhiteSpace(S(e, "buyer_tax_no")) ? null : S(e, "buyer_tax_no"),
            Join(S(e, "buyer_street"), buyerCity), string.IsNullOrWhiteSpace(S(e, "buyer_email")) ? null : S(e, "buyer_email"),
            Join(S(e, "seller_name"), S(e, "seller_street"), sellerCity, string.IsNullOrWhiteSpace(sellerTax) ? null : $"NIP: {sellerTax}") ?? "",
            net, gross - net, gross, S(e, "currency") ?? "PLN", S(e, "payment_type"), S(e, "view_url"), S(e, "oid"),
            S(e, "exempt_tax_kind"), S(e, "description"), S(e, "invoice_id") ?? S(e, "from_invoice_id"), lines);
    }

    /// <summary>Treść „invoice” dla API Fakturowni (sprzedawca — z ustawień konta w Fakturowni).</summary>
    public static Dictionary<string, object?> Payload(SalesDocument doc, SalesDocument? original)
    {
        static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        static string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        var (street, postCode, city) = SplitAddress(doc.BuyerAddress);
        var isBill = doc.Kind == SalesDocumentKinds.Receipt;
        var p = new Dictionary<string, object?>
        {
            ["kind"] = doc.Kind switch
            {
                SalesDocumentKinds.Correction => "correction",
                SalesDocumentKinds.Receipt => "bill",
                _ => "vat"
            },
            ["number"] = null,
            ["issue_date"] = D(doc.IssueDate),
            ["sell_date"] = D(doc.SaleDate),
            ["payment_to"] = D(doc.PaidDate ?? doc.IssueDate),
            ["payment_type"] = PaymentType(doc.PaymentMethod),
            ["currency"] = doc.Currency,
            ["lang"] = "pl",
            ["buyer_name"] = doc.BuyerName,
            ["buyer_tax_no"] = string.IsNullOrWhiteSpace(doc.BuyerTaxId) ? null : doc.BuyerTaxId,
            ["buyer_company"] = !string.IsNullOrWhiteSpace(doc.BuyerTaxId),
            ["buyer_street"] = street,
            ["buyer_post_code"] = postCode,
            ["buyer_city"] = city,
            ["buyer_email"] = doc.BuyerEmail,
            ["description"] = doc.Note,
            ["oid"] = $"hantly-{doc.Id}",
        };
        if (doc.PaidDate is { } paid)
        {
            p["status"] = "paid";
            p["paid_date"] = D(paid);
        }
        if (!string.IsNullOrWhiteSpace(doc.VatNote) && doc.Kind != SalesDocumentKinds.InvoiceVat) p["exempt_tax_kind"] = doc.VatNote;

        if (doc.Kind == SalesDocumentKinds.Correction && original is not null)
        {
            p["invoice_id"] = original.ExternalId;
            p["from_invoice_id"] = original.ExternalId;
            p["correction_reason"] = doc.CorrectionReason;
            var tax = original.Lines.FirstOrDefault()?.VatRate ?? "zw";
            var name = original.Lines.Count == 1 ? original.Lines[0].Name : $"Korekta dokumentu {original.Number}";
            var change = doc.Gross; // ujemna
            p["positions"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = name, ["quantity"] = 1, ["total_price_gross"] = M(change), ["tax"] = tax, ["kind"] = "correction",
                    ["correction_before_attributes"] = new Dictionary<string, object?>
                        { ["name"] = name, ["quantity"] = 1, ["total_price_gross"] = M(original.Gross), ["tax"] = tax, ["kind"] = "correction_before" },
                    ["correction_after_attributes"] = new Dictionary<string, object?>
                        { ["name"] = name, ["quantity"] = 1, ["total_price_gross"] = M(original.Gross + change), ["tax"] = tax, ["kind"] = "correction_after" },
                }
            };
            return p;
        }

        p["positions"] = doc.Lines.Select(l =>
        {
            var pos = new Dictionary<string, object?>
            {
                ["name"] = l.Name,
                ["quantity"] = l.Quantity,
                ["quantity_unit"] = l.Unit,
                ["total_price_gross"] = M(l.Gross),
            };
            if (!isBill) pos["tax"] = l.VatRate;
            return pos;
        }).ToArray();
        return p;
    }

    private static string PaymentType(string? method) => (method ?? "").ToLowerInvariant() switch
    {
        var m when m.Contains("kart") || m.Contains("stripe") => "card",
        var m when m.Contains("gotów") => "cash",
        _ => "transfer"
    };

    /// <summary>Adres w kilku liniach → ulica, kod pocztowy, miasto (kod szukany w ostatniej linii).</summary>
    public static (string? Street, string? PostCode, string? City) SplitAddress(string? address)
    {
        var lines = (address ?? "").Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return (null, null, null);
        var last = lines[^1];
        var m = Regex.Match(last, @"^(\d{2}-\d{3})\s+(.+)$");
        if (!m.Success) return (string.Join(", ", lines), null, null);
        var street = lines.Length > 1 ? string.Join(", ", lines[..^1]) : null;
        return (street, m.Groups[1].Value, m.Groups[2].Value);
    }

    private static string Shorten(string s)
    {
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return s.Length > 300 ? s[..300] + "…" : s;
    }
}
