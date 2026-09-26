using System.Globalization;
using System.Xml.Linq;

namespace PTScheduler.Application.Ksef;

/// <summary>
/// Buduje XML faktury ustrukturyzowanej w schemacie FA(3) — formacie przyjmowanym
/// przez KSeF od 1 lutego 2026. Obsługuje przypadki typowe dla trenera personalnego:
/// sprzedaż opodatkowaną jedną stawką (kwoty brutto) albo zwolnioną z VAT
/// (np. zwolnienie podmiotowe z art. 113), nabywcę-firmę (NIP) lub konsumenta.
///
/// Uwaga: przed pierwszym użyciem produkcyjnym wyślij fakturę na środowisko
/// testowe KSeF — tam MF waliduje XML ze schematem XSD i zwraca szczegóły błędów.
/// </summary>
public static class Fa3XmlBuilder
{
    // Przestrzeń nazw schematu FA(3) (wzór crd.gov.pl). Kod i wersja schematu
    // są przesyłane także przy otwieraniu sesji w KSeF (formCode).
    public const string Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";
    public const string SystemCode = "FA (3)";
    public const string SchemaVersion = "1-0E";
    public const string FormValue = "FA";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Build(Fa3Invoice inv, DateTime createdUtc, string systemInfo = "PTScheduler")
    {
        if (inv.Lines.Count == 0) throw new ArgumentException("Faktura musi mieć co najmniej jedną pozycję.");
        if (!Nip.IsValid(inv.Seller.Nip)) throw new ArgumentException("Nieprawidłowy NIP sprzedawcy.");

        XNamespace ns = Namespace;
        var gross = inv.Lines.Sum(l => Round(l.Quantity * l.UnitGross));
        var rate = inv.VatPayer ? inv.VatRate : 0m;
        var net = inv.VatPayer ? Round(gross / (1 + rate / 100m)) : gross;
        var vat = gross - net;

        var fa = new XElement(ns + "Fa",
            new XElement(ns + "KodWaluty", inv.Currency),
            new XElement(ns + "P_1", Date(inv.IssueDate)),
            new XElement(ns + "P_2", inv.Number),
            inv.SaleDate != inv.IssueDate ? new XElement(ns + "P_6", Date(inv.SaleDate)) : null);

        if (inv.VatPayer)
        {
            fa.Add(new XElement(ns + RateNetField(rate), Money(net)),
                   new XElement(ns + RateVatField(rate), Money(vat)));
        }
        else
        {
            // Sprzedaż zwolniona z VAT.
            fa.Add(new XElement(ns + "P_13_7", Money(gross)));
        }
        fa.Add(new XElement(ns + "P_15", Money(gross)));

        fa.Add(new XElement(ns + "Adnotacje",
            new XElement(ns + "P_16", 2),   // metoda kasowa — nie
            new XElement(ns + "P_17", 2),   // samofakturowanie — nie
            new XElement(ns + "P_18", 2),   // odwrotne obciążenie — nie
            new XElement(ns + "P_18A", 2),  // mechanizm podzielonej płatności — nie
            new XElement(ns + "Zwolnienie",
                inv.VatPayer
                    ? (object)new XElement(ns + "P_19N", 1)
                    : new object[]
                    {
                        new XElement(ns + "P_19", 1),
                        new XElement(ns + "P_19A", string.IsNullOrWhiteSpace(inv.VatExemptBasis)
                            ? "art. 113 ust. 1 ustawy o VAT" : inv.VatExemptBasis)
                    }),
            new XElement(ns + "NoweSrodkiTransportu", new XElement(ns + "P_22N", 1)),
            new XElement(ns + "P_23", 2),   // procedura uproszczona — nie
            new XElement(ns + "PMarzy", new XElement(ns + "P_PMarzyN", 1))));

        fa.Add(new XElement(ns + "RodzajFaktury", "VAT"));

        var no = 1;
        foreach (var l in inv.Lines)
        {
            fa.Add(new XElement(ns + "FaWiersz",
                new XElement(ns + "NrWierszaFa", no++),
                new XElement(ns + "P_7", Truncate(l.Name, 512)),
                new XElement(ns + "P_8A", l.Unit),
                new XElement(ns + "P_8B", l.Quantity.ToString("0.######", Inv)),
                // Ceny w aplikacji są brutto — pola „B” (cena i wartość brutto).
                new XElement(ns + "P_9B", Money(l.UnitGross)),
                new XElement(ns + "P_11A", Money(Round(l.Quantity * l.UnitGross))),
                new XElement(ns + "P_12", inv.VatPayer ? rate.ToString("0.##", Inv) : "zw")));
        }

        if (inv.PaidDate is { } paid)
        {
            fa.Add(new XElement(ns + "Platnosc",
                new XElement(ns + "Zaplacono", 1),
                new XElement(ns + "DataZaplaty", Date(paid))));
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "Faktura",
                new XElement(ns + "Naglowek",
                    new XElement(ns + "KodFormularza",
                        new XAttribute("kodSystemowy", SystemCode),
                        new XAttribute("wersjaSchemy", SchemaVersion),
                        FormValue),
                    new XElement(ns + "WariantFormularza", 3),
                    new XElement(ns + "DataWytworzeniaFa", createdUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", Inv)),
                    new XElement(ns + "SystemInfo", systemInfo)),
                Party(ns, "Podmiot1", inv.Seller, isBuyer: false),
                Party(ns, "Podmiot2", inv.Buyer, isBuyer: true),
                fa));

        using var sw = new Utf8StringWriter();
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }

    private static XElement Party(XNamespace ns, string name, Fa3Party p, bool isBuyer)
    {
        var id = new XElement(ns + "DaneIdentyfikacyjne");
        var nip = Nip.Normalize(p.Nip);
        if (nip.Length == 10) id.Add(new XElement(ns + "NIP", nip));
        else id.Add(new XElement(ns + "BrakID", 1));   // konsument bez NIP
        id.Add(new XElement(ns + "Nazwa", Truncate(p.Name, 512)));

        var el = new XElement(ns + name, id);
        if (!string.IsNullOrWhiteSpace(p.AddressLine1))
        {
            el.Add(new XElement(ns + "Adres",
                new XElement(ns + "KodKraju", "PL"),
                new XElement(ns + "AdresL1", Truncate(p.AddressLine1!, 512)),
                string.IsNullOrWhiteSpace(p.AddressLine2) ? null : new XElement(ns + "AdresL2", Truncate(p.AddressLine2!, 512))));
        }
        if (isBuyer)
        {
            // FA(3): nabywca nie jest jednostką samorządu (JST) ani członkiem grupy VAT (GV).
            el.Add(new XElement(ns + "JST", 2), new XElement(ns + "GV", 2));
        }
        return el;
    }

    // P_13_1/P_14_1 — stawka podstawowa (23/22), P_13_2/P_14_2 — obniżona I (8/7),
    // P_13_3/P_14_3 — obniżona II (5).
    private static string RateNetField(decimal rate) => rate switch
    {
        23m or 22m => "P_13_1",
        8m or 7m => "P_13_2",
        5m => "P_13_3",
        _ => throw new ArgumentException($"Nieobsługiwana stawka VAT: {rate}%.")
    };

    private static string RateVatField(decimal rate) => RateNetField(rate).Replace("P_13_", "P_14_");

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string Money(decimal v) => Round(v).ToString("0.00", Inv);
    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
    }
}
