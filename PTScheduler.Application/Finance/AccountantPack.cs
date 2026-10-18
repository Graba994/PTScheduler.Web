using System.Globalization;
using System.Text;

namespace PTScheduler.Application.Finance;

/// <summary>
/// „Paczka dla księgowej”: wpływy z jednego miesiąca w trzech grupach — płatności online, opłacone u trenera
/// (gotówka, przelew) i informacyjnie: sprzedane, a jeszcze nieopłacone pakiety. Tu tylko model i CSV (bez bazy).
/// </summary>
public static class AccountantPack
{
    public const string Online = "Płatność online";
    public const string AtTrainer = "Opłacone u trenera";
    public const string Unpaid = "Nieopłacone (informacyjnie)";

    public sealed record Row(DateTime Date, string Group, string Kind, string Description, string Client, decimal Amount,
        string Currency, string Method, string? Invoice = null, string? BuyerNip = null, string? Ksef = null);

    public sealed record Pack(int Year, int Month, string Seller, IReadOnlyList<Row> Rows)
    {
        public IEnumerable<Row> InGroup(string group) => Rows.Where(r => r.Group == group).OrderBy(r => r.Date);
        public decimal Total(string group) => Rows.Where(r => r.Group == group).Sum(r => r.Amount);
        /// <summary>Wpływy w miesiącu (bez nieopłaconych).</summary>
        public decimal Received => Total(Online) + Total(AtTrainer);
        public int Invoices => Rows.Count(r => !string.IsNullOrEmpty(r.Invoice));
        public string Currency => Rows.Select(r => r.Currency).FirstOrDefault() ?? "PLN";
        public string MonthName => MonthLabel(Year, Month);
        public string FileStem => $"ksiegowa-{Year}-{Month:00}";
    }

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly string[] Months =
        ["styczeń", "luty", "marzec", "kwiecień", "maj", "czerwiec", "lipiec", "sierpień", "wrzesień", "październik", "listopad", "grudzień"];

    /// <summary>„wrzesień 2026” (mianownik — .NET daje tylko dopełniacz „września”).</summary>
    public static string MonthLabel(int year, int month) => $"{Months[month - 1]} {year}";

    public static string Money(decimal amount, string currency) => $"{amount.ToString("N2", Pl)} {currency}";

    /// <summary>CSV dla Excela po polsku: średnik, przecinek dziesiętny, UTF-8 z BOM.</summary>
    public static byte[] Csv(Pack pack)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Data;Grupa;Rodzaj;Opis;Klient;Kwota;Waluta;Sposób zapłaty;Nr faktury;NIP nabywcy;Nr KSeF");
        foreach (var group in new[] { Online, AtTrainer, Unpaid })
            foreach (var r in pack.InGroup(group))
                sb.AppendLine(string.Join(';',
                    r.Date.ToString("yyyy-MM-dd"), Cell(r.Group), Cell(r.Kind), Cell(r.Description), Cell(r.Client),
                    r.Amount.ToString("0.00", Pl), r.Currency, Cell(r.Method), Cell(r.Invoice), Cell(r.BuyerNip), Cell(r.Ksef)));
        sb.AppendLine();
        sb.AppendLine($"Razem online;;;;;{pack.Total(Online).ToString("0.00", Pl)};{pack.Currency}");
        sb.AppendLine($"Razem u trenera;;;;;{pack.Total(AtTrainer).ToString("0.00", Pl)};{pack.Currency}");
        sb.AppendLine($"Wpływy razem;;;;;{pack.Received.ToString("0.00", Pl)};{pack.Currency}");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>Komórka CSV: cudzysłów, gdy trzeba; bez formuł (=, +, -, @ na początku) — ochrona przed wstrzyknięciem do Excela.</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value.Replace("\r", " ").Replace("\n", " ");
        if (v[0] is '=' or '+' or '-' or '@') v = "'" + v;
        return v.IndexOfAny([';', '"']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    /// <summary>Poprzedni miesiąc względem dnia <paramref name="today"/> — to ten wysyłamy automatycznie.</summary>
    public static (int Year, int Month) PreviousMonth(DateOnly today)
    {
        var d = today.AddMonths(-1);
        return (d.Year, d.Month);
    }
}
