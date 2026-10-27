using System.Text;
using System.Text.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// Pomoc zespołu platformy zamawiana z „Pierwszych ustawień” (/witaj): podłączenie skrzynki i strona pod klucz. To pozycje sklepu usług w Portalu — cena z katalogu (z ustaleniami dla tego studia),
/// zamówienie trafia do Zgłoszeń w Portalu, a kwota na rachunek dopiero po wykonaniu.
/// </summary>
public sealed class PlatformHelpService(ILogger<PlatformHelpService> logger)
{
    public const int MailSetup = 9;
    public const int SiteSetup = 10;

    public sealed record Offer(int Id, string Name, decimal Price);

    private static (string Url, string Slug, string? Secret)? Portal()
    {
        var url = Environment.GetEnvironmentVariable("PORTAL_URL");
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG");
        return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(slug)
            ? null : (url.TrimEnd('/'), slug, Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET"));
    }

    /// <summary>Instancja połączona z Portalem (bez tego nie ma komu zamówić pomocy).</summary>
    public bool Available => Portal() is not null;

    /// <summary>Usługi pomocy dostępne dla tego studia (ukryte w Portalu — pomijamy).</summary>
    public async Task<Dictionary<int, Offer>> OffersAsync()
    {
        var result = new Dictionary<int, Offer>();
        if (Portal() is not { } p) return result;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{p.Url}/api/store/{p.Slug}");
            if (!string.IsNullOrEmpty(p.Secret)) req.Headers.TryAddWithoutValidation("X-Internal-Secret", p.Secret);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return result;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("catalog", out var catalog)) return result;
            foreach (var el in catalog.EnumerateArray())
            {
                var id = el.GetProperty("id").GetInt32();
                if (id is MailSetup or SiteSetup)
                    result[id] = new(id, el.GetProperty("name").GetString() ?? "", el.GetProperty("price").GetDecimal());
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Nie udało się pobrać usług pomocy z Portalu."); }
        return result;
    }

    /// <summary>Zamówienie bez płatności online: zgłoszenie dla zespołu, kwota na rachunek po wykonaniu.</summary>
    public async Task<(bool Ok, string? Error)> OrderAsync(int serviceItemId, string notes)
    {
        if (Portal() is not { } p) return (false, "Aplikacja nie jest połączona z platformą — napisz do nas z Pomocy.");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{p.Url}/api/store/{p.Slug}/order");
            req.Content = new StringContent(JsonSerializer.Serialize(new { items = new[] { serviceItemId }, notes }), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(p.Secret)) req.Headers.TryAddWithoutValidation("X-Internal-Secret", p.Secret);
            using var resp = await http.SendAsync(req);
            return resp.IsSuccessStatusCode ? (true, null) : (false, $"Nie udało się wysłać zamówienia (HTTP {(int)resp.StatusCode}). Spróbuj za chwilę.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Zamówienie pomocy {Item} nie dotarło do Portalu.", serviceItemId);
            return (false, "Nie udało się połączyć z platformą. Spróbuj za chwilę.");
        }
    }

    /// <summary>„49 zł” albo „w cenie” (ustalenia z zespołem: cena 0).</summary>
    public static string PriceText(decimal price) =>
        price <= 0 ? "w cenie" : $"{price.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"))} zł";
}
