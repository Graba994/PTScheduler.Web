using System.Globalization;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Reguły księgowe bez bazy: limit działalności nierejestrowanej, kwartały, VAT w pozycjach, numeracja.
/// Przepisy się zmieniają — kwoty są tu domyślne, a każdą da się nadpisać w ustawieniach księgowości.
/// </summary>
public static class FinanceRules
{
    /// <summary>
    /// Minimalne wynagrodzenie (brutto) obowiązujące w danym kwartale. Limit działalności nierejestrowanej
    /// to 225% tej kwoty na kwartał (art. 5 ust. 1 Prawa przedsiębiorców, od 2024 r.).
    /// </summary>
    public static decimal? MinimumWage(int year, int quarter) => year switch
    {
        2024 => quarter <= 2 ? 4242m : 4300m,
        2025 => 4666m,
        2026 => 4806m,
        _ => null
    };

    public const decimal LimitFactor = 2.25m;

    /// <summary>Limit przychodu w kwartale. Estimated = rok spoza tabeli (bierzemy ostatnią znaną płacę — sprawdź i wpisz limit).</summary>
    public static (decimal Limit, bool Estimated) QuarterLimit(int year, int quarter, decimal? custom = null)
    {
        if (custom is > 0) return (custom.Value, false);
        if (MinimumWage(year, quarter) is { } wage) return (Math.Round(wage * LimitFactor, 2), false);
        var last = MinimumWage(2026, 4)!.Value;
        return (Math.Round(last * LimitFactor, 2), true);
    }

    public static int QuarterOf(DateOnly d) => (d.Month - 1) / 3 + 1;
    public static DateOnly QuarterStart(int year, int quarter) => new(year, (quarter - 1) * 3 + 1, 1);
    public static DateOnly QuarterEnd(int year, int quarter) => QuarterStart(year, quarter).AddMonths(3).AddDays(-1);

    /// <summary>Pierwszy dzień, w którym suma przychodu od początku kwartału przekroczyła limit (null — nie przekroczyła).</summary>
    public static DateOnly? ExceededOn(IEnumerable<(DateOnly Date, decimal Amount)> entries, decimal limit)
    {
        decimal sum = 0;
        foreach (var day in entries.GroupBy(e => e.Date).OrderBy(g => g.Key))
        {
            sum += day.Sum(e => e.Amount);
            if (sum > limit) return day.Key;
        }
        return null;
    }

    /// <summary>Do kiedy złożyć wniosek do CEIDG po przekroczeniu limitu (7 dni od dnia przekroczenia).</summary>
    public static DateOnly CeidgDeadline(DateOnly exceededOn) => exceededOn.AddDays(7);

    /// <summary>Prognoza na koniec kwartału: tempo dzienne dotychczasowej sprzedaży razy długość kwartału.</summary>
    public static decimal Forecast(decimal soFar, DateOnly quarterStart, DateOnly quarterEnd, DateOnly today)
    {
        if (today < quarterStart) return 0;
        if (today >= quarterEnd) return soFar;
        var elapsed = today.DayNumber - quarterStart.DayNumber + 1;
        var total = quarterEnd.DayNumber - quarterStart.DayNumber + 1;
        return Math.Round(soFar / elapsed * total, 2);
    }

    /// <summary>„ok” do 80% limitu, „warn” do 100%, „danger” powyżej.</summary>
    public static string Tone(decimal value, decimal limit) =>
        limit <= 0 ? "muted" : value > limit ? "danger" : value >= limit * 0.8m ? "warn" : "ok";

    /// <summary>Limit zwolnienia z VAT (podmiotowego) w roku — proporcjonalnie, gdy firma działa od połowy roku.</summary>
    public static decimal VatLimitFor(int year, decimal yearlyLimit, DateOnly? businessSince)
    {
        if (businessSince is not { } since || since.Year < year) return yearlyLimit;
        if (since.Year > year) return 0;
        var days = DateTime.IsLeapYear(year) ? 366 : 365;
        var active = new DateOnly(year, 12, 31).DayNumber - since.DayNumber + 1;
        return Math.Round(yearlyLimit * active / days, 2);
    }

    /// <summary>Domyślny limit zwolnienia z VAT (od 2026 r. 240 000 zł; wcześniej 200 000 zł).</summary>
    public static decimal DefaultVatLimit(int year) => year >= 2026 ? 240_000m : 200_000m;

    /// <summary>Netto i VAT z kwoty brutto dla stawki („zw”, „np”, „0” — bez VAT).</summary>
    public static (decimal Net, decimal Vat) Split(decimal gross, string rate)
    {
        if (!int.TryParse(rate, out var pct) || pct <= 0) return (gross, 0);
        var net = Math.Round(gross / (1 + pct / 100m), 2, MidpointRounding.AwayFromZero);
        return (net, gross - net);
    }

    public static string FormatNumber(string prefix, int year, int seq) =>
        $"{(string.IsNullOrWhiteSpace(prefix) ? "DOK" : prefix.Trim().ToUpperInvariant())}/{year}/{seq:0000}";

    /// <summary>Rodzaj dokumentu po wpłacie: firma z NIP-em dostaje fakturę, osoba prywatna rachunek; czynny VAT — zawsze fakturę VAT.</summary>
    public static string KindFor(string mode, string vat, string? buyerTaxId)
    {
        if (mode == FinanceModes.Business && vat == "vat") return SalesDocumentKinds.InvoiceVat;
        return string.IsNullOrWhiteSpace(buyerTaxId) ? SalesDocumentKinds.Receipt : SalesDocumentKinds.InvoiceExempt;
    }

    public static string DefaultVatNote(string mode, string kind) => kind == SalesDocumentKinds.InvoiceVat ? ""
        : mode == FinanceModes.Unregistered
            ? "Sprzedaż zwolniona z VAT na podstawie art. 113 ust. 1 i 9 ustawy o VAT. Działalność nierejestrowana (art. 5 ust. 1 ustawy Prawo przedsiębiorców)."
            : "Sprzedaż zwolniona z VAT na podstawie art. 113 ust. 1 ustawy o VAT.";

    /// <summary>Tryb obowiązujący w danym dniu (po dacie założenia firmy — firma).</summary>
    public static string ModeOn(DateOnly date, string currentMode, DateOnly? businessSince) =>
        currentMode == FinanceModes.Business && businessSince is { } since && date < since ? FinanceModes.Unregistered : currentMode;

    /// <summary>Prosta kontrola NIP (10 cyfr z sumą kontrolną). Pusty NIP jest poprawny — to osoba prywatna.</summary>
    public static bool IsValidNip(string? nip)
    {
        var digits = new string((nip ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(nip)) return true;
        if (nip.Trim().StartsWith("PL", StringComparison.OrdinalIgnoreCase)) digits = new string(nip.Trim()[2..].Where(char.IsDigit).ToArray());
        if (digits.Length != 10) return false;
        int[] w = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var sum = w.Select((x, i) => x * (digits[i] - '0')).Sum();
        return sum % 11 == digits[9] - '0';
    }

    public static string CleanNip(string? nip) => new((nip ?? "").Where(char.IsDigit).ToArray());

    public static decimal? ParseAmount(string? s) =>
        decimal.TryParse(new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static string Money(decimal v) => v.ToString("#,##0.00", Pl) + " zł";

    public static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Słownie kwoty (zł i gr) — drukowane na dokumencie.</summary>
    public static string InWords(decimal amount)
    {
        var neg = amount < 0;
        amount = Math.Abs(Math.Round(amount, 2));
        var zl = (long)Math.Floor(amount);
        var gr = (int)Math.Round((amount - zl) * 100);
        var words = zl == 0 ? "zero" : Words(zl);
        return $"{(neg ? "minus " : "")}{words} {Plural(zl, "złoty", "złote", "złotych")} {gr:00}/100";
    }

    private static readonly string[] Units = ["", "jeden", "dwa", "trzy", "cztery", "pięć", "sześć", "siedem", "osiem", "dziewięć"];
    private static readonly string[] Teens = ["dziesięć", "jedenaście", "dwanaście", "trzynaście", "czternaście", "piętnaście", "szesnaście", "siedemnaście", "osiemnaście", "dziewiętnaście"];
    private static readonly string[] Tens = ["", "", "dwadzieścia", "trzydzieści", "czterdzieści", "pięćdziesiąt", "sześćdziesiąt", "siedemdziesiąt", "osiemdziesiąt", "dziewięćdziesiąt"];
    private static readonly string[] Hundreds = ["", "sto", "dwieście", "trzysta", "czterysta", "pięćset", "sześćset", "siedemset", "osiemset", "dziewięćset"];
    private static readonly (string One, string Few, string Many)[] Groups =
        [("", "", ""), ("tysiąc", "tysiące", "tysięcy"), ("milion", "miliony", "milionów"), ("miliard", "miliardy", "miliardów")];

    private static string Words(long n)
    {
        var parts = new List<string>();
        var g = 0;
        while (n > 0 && g < Groups.Length)
        {
            var chunk = (int)(n % 1000);
            if (chunk > 0)
            {
                var text = chunk == 1 && g > 0 ? "" : Chunk(chunk);
                var name = g == 0 ? "" : Plural(chunk, Groups[g].One, Groups[g].Few, Groups[g].Many);
                parts.Insert(0, string.Join(" ", new[] { text, name }.Where(x => x.Length > 0)));
            }
            n /= 1000;
            g++;
        }
        return string.Join(" ", parts);
    }

    private static string Chunk(int n)
    {
        var h = n / 100; var rest = n % 100; var t = rest / 10; var u = rest % 10;
        var list = new List<string> { Hundreds[h] };
        if (t == 1) list.Add(Teens[u]);
        else { list.Add(Tens[t]); list.Add(Units[u]); }
        return string.Join(" ", list.Where(x => x.Length > 0));
    }

    public static string Plural(long n, string one, string few, string many)
    {
        if (n == 1) return one;
        var d = n % 10; var dd = n % 100;
        return d is >= 2 and <= 4 && dd is < 12 or > 14 ? few : many;
    }
}
