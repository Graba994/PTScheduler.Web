using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Dokumenty prawne platformy: regulamin, polityka prywatności i umowa powierzenia (załącznik nr 1).
/// Treść leży w plikach Legal/*.html (zasoby wbudowane), dane usługodawcy są wstawiane z Księgowości (jedno źródło:
/// imię i nazwisko albo firma, adres, NIP, forma działalności), a marka, e-mail i podmioty przetwarzające — z ustawień Panelu.
/// Bloki {{?NIEREJ}}…{{/NIEREJ}}, {{?FIRMA}}, {{?BEZVAT}}, {{?VAT}} zależą od formy działalności.
/// Akceptacje (kto, którą wersję, kiedy, IP) trafiają do LegalAcceptances — to dowód zawarcia umowy.
/// </summary>
public class LegalService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    IConfiguration config)
{
    public sealed record Doc(string Key, string Title, string Path, string Version, string File);

    /// <summary>Zmiana wersji = trenerzy muszą zaakceptować dokument ponownie (przy kolejnym logowaniu).</summary>
    public static readonly Doc[] Docs =
    [
        new("regulamin", "Regulamin", "/regulamin", "2026-10-26", "regulamin.html"),
        new("powierzenie", "Umowa powierzenia przetwarzania danych", "/umowa-powierzenia", "2026-10-26", "umowa-powierzenia.html"),
        new("prywatnosc", "Polityka prywatności", "/polityka-prywatnosci", "2026-10-28", "polityka-prywatnosci.html"),
    ];

    public static Doc Get(string key) => Docs.First(d => d.Key == key);

    private static readonly Dictionary<string, string> Raw = Docs.ToDictionary(d => d.Key, d =>
    {
        using var stream = typeof(LegalService).Assembly.GetManifestResourceStream("Legal." + d.File)
            ?? throw new InvalidOperationException($"Brak zasobu Legal/{d.File}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <param name="Mode">„unregistered”, „business” albo pusty (forma nieznana — Księgowość nieustawiona).</param>
    /// <param name="FromAccounting">Nazwa, adres i NIP pochodzą z Księgowości (zmienia się je tam).</param>
    public sealed record Company(string Brand, string Name, string Address, string Nip, string Email, string Hosting, string Mail,
        string Mode = "", bool FromAccounting = false, bool VatPayer = false)
    {
        public bool Unregistered => Mode == FinanceModes.Unregistered;
        public bool Business => Mode == FinanceModes.Business || (Mode.Length == 0 && Nip.Length > 0);
        public bool Complete => Name.Length > 0 && Address.Length > 0 && (Unregistered || Nip.Length > 0) && Email.Length > 0 && Hosting.Length > 0 && Mail.Length > 0;

        /// <summary>Co stoi w dokumentach po nazwie i adresie: NIP firmy albo informacja o działalności nierejestrowanej.</summary>
        public string? Identification => Unregistered
            ? "osoba fizyczna prowadząca działalność nierejestrowaną (art. 5 ust. 1 ustawy Prawo przedsiębiorców)"
            : Nip.Length > 0 ? $"NIP {Nip}" : null;
    }

    public async Task<Company> CompanyAsync()
    {
        var s = await settings.GetAllAsync(K.BrandName, K.LegalCompany, K.LegalAddress, K.LegalNip, K.LegalEmail, K.LegalHosting, K.LegalMail,
            K.FinanceMode, K.FinanceBusinessSince, K.FinanceSellerName, K.FinanceSellerAddress, K.FinanceSellerTaxId, K.FinanceSellerEmail, K.FinanceVat);
        string V(string k) => s.TryGetValue(k, out var v) ? v.Trim() : "";
        var brand = V(K.BrandName) is { Length: > 0 } b ? b : ReferralService.PlatformName;
        var mode = V(K.FinanceMode);
        if (mode is FinanceModes.Unregistered or FinanceModes.Business)
        {
            // Przed datą z CEIDG obowiązuje jeszcze działalność nierejestrowana.
            DateOnly? since = DateOnly.TryParseExact(V(K.FinanceBusinessSince), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
            var today = AccountingService.Today;
            var current = FinanceRules.ModeOn(today, mode, since);
            var address = string.Join(", ", V(K.FinanceSellerAddress).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return new Company(brand, V(K.FinanceSellerName), address,
                current == FinanceModes.Business ? V(K.FinanceSellerTaxId) : "",
                V(K.LegalEmail) is { Length: > 0 } e ? e : V(K.FinanceSellerEmail),
                V(K.LegalHosting), V(K.LegalMail), current, FromAccounting: true,
                VatPayer: current == FinanceModes.Business && V(K.FinanceVat) == "vat");
        }
        return new Company(brand, V(K.LegalCompany), V(K.LegalAddress), V(K.LegalNip), V(K.LegalEmail), V(K.LegalHosting), V(K.LegalMail));
    }

    private static readonly Regex Block = new(@"\{\{\?(?<k>[A-Z]+)\}\}(?<body>.*?)\{\{/\k<k>\}\}", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Zostawia bloki pasujące do formy działalności, usuwa pozostałe.</summary>
    public static string ApplyBlocks(string html, Company c)
    {
        // Forma nieznana (Księgowość nieustawiona) — wariant firmowy i ogólny zapis o VAT, jak przed rozróżnieniem form.
        var noVat = c.FromAccounting && !c.VatPayer;
        var on = new Dictionary<string, bool>
        {
            ["NIEREJ"] = c.Unregistered,
            ["FIRMA"] = !c.Unregistered,
            ["BEZVAT"] = noVat,
            ["VAT"] = !noVat,
        };
        return Block.Replace(html, m => on.GetValueOrDefault(m.Groups["k"].Value) ? m.Groups["body"].Value : "");
    }

    /// <summary>Gotowy HTML dokumentu z danymi firmy; puste pola są wyraźnie oznaczone do uzupełnienia.</summary>
    public async Task<string> RenderAsync(string key)
    {
        var doc = Get(key);
        var c = await CompanyAsync();
        var site = config.GetValue<string>("Portal:PublicUrl")?.TrimEnd('/') is { Length: > 0 } pub ? pub : "https://hantly.pl";
        var domain = await settings.GetAsync(K.MainDomain) is { Length: > 0 } d ? d : new Uri(site).Host;
        var date = DateTime.ParseExact(doc.Version, "yyyy-MM-dd", null).ToString("d MMMM yyyy", new System.Globalization.CultureInfo("pl-PL"));

        static string Fill(string value, string missing) =>
            value.Length > 0 ? WebUtility.HtmlEncode(value) : $"<mark class=\"lg-missing\">[{missing}]</mark>";

        return ApplyBlocks(Raw[key], c)
            .Replace("{{IDENTYFIKACJA}}", c.Identification is { } id ? WebUtility.HtmlEncode(id) : Fill("", "NIP albo forma działalności — ustaw w Księgowości"))
            .Replace("{{MARKA}}", WebUtility.HtmlEncode(c.Brand))
            .Replace("{{STRONA}}", WebUtility.HtmlEncode(site))
            .Replace("{{DOMENA}}", WebUtility.HtmlEncode(domain))
            .Replace("{{FIRMA}}", Fill(c.Name, "nazwa firmy"))
            .Replace("{{ADRES}}", Fill(c.Address, "adres"))
            .Replace("{{NIP}}", Fill(c.Nip, "NIP"))
            .Replace("{{EMAIL}}", Fill(c.Email, "e-mail"))
            .Replace("{{HOSTING}}", Fill(c.Hosting, "dostawca serwerów"))
            .Replace("{{POCZTA}}", Fill(c.Mail, "dostawca e-maili"))
            .Replace("{{DATA}}", date)
            .Replace("{{WERSJA}}", doc.Version);
    }

    public async Task RecordAsync(int? tenantId, string email, IEnumerable<string> keys, string? ip, string? userAgent, string source)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var key in keys.Distinct())
        {
            var doc = Get(key);
            db.LegalAcceptances.Add(new LegalAcceptance
            {
                TenantId = tenantId,
                Email = Clip(email, 256) ?? "",
                DocKey = doc.Key,
                Version = doc.Version,
                Ip = Clip(ip, 64),
                UserAgent = Clip(userAgent, 400),
                Source = source
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Dokumenty, których aktualnej wersji trener jeszcze nie zaakceptował.</summary>
    public async Task<List<Doc>> PendingForTenantAsync(int tenantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var accepted = await db.LegalAcceptances.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.DocKey + "@" + a.Version)
            .Distinct()
            .ToListAsync();
        return Docs.Where(d => !accepted.Contains(d.Key + "@" + d.Version)).ToList();
    }

    private static string? Clip(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(v.Trim().Length, max)];
}
