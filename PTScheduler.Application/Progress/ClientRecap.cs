using System.Globalization;

namespace PTScheduler.Application.Progress;

/// <summary>Dane do „Twój miesiąc” / „Twój rok” — policzone z historii klienta za okres i okres wcześniej.</summary>
/// <param name="From">Pierwszy dzień okresu (1. dzień miesiąca albo 1 stycznia).</param>
/// <param name="TrainingDays">Dni z treningiem w okresie (odbyta wizyta albo zapis w dzienniku).</param>
/// <param name="TrainingDaysPrevious">To samo w poprzednim okresie (miesiąc albo rok wcześniej).</param>
/// <param name="WeekStreak">Ile tygodni z rzędu z treningiem na koniec okresu.</param>
/// <param name="BestStreak">Najdłuższa seria tygodni w okresie (dla roku).</param>
/// <param name="ActiveMonths">W ilu miesiącach był co najmniej jeden trening (dla roku).</param>
/// <param name="TopRecord">Najlepszy rekord okresu, np. („Przysiad ze sztangą”, 80).</param>
/// <param name="TopGain">Największy przyrost w okresie (dla roku), np. („Wyciskanie”, 50, 65).</param>
public sealed record RecapInput(
    bool IsYear,
    DateOnly From,
    int TrainingDays,
    int TrainingDaysPrevious,
    int WeekStreak,
    int BestStreak,
    int ActiveMonths,
    int Records,
    (string Exercise, decimal Kg)? TopRecord,
    (string Exercise, decimal FromKg, decimal ToKg)? TopGain,
    IReadOnlyList<string> Medals);

public sealed record RecapTile(string Value, string Label, string Icon);

public sealed record RecapResult(string Title, string Subtitle, List<RecapTile> Tiles, List<string> Highlights,
    Dictionary<string, string> Facts, string Fallback, string PushBody)
{
    public bool HasData => Tiles.Count > 0;
}

/// <summary>
/// „Twój miesiąc” (1. dnia miesiąca za poprzedni) i „Twój rok” (w styczniu za poprzedni rok) dla klienta —
/// pod marką trenera, w drugiej osobie i formach neutralnych płciowo. Bez oceniania wagi i bez imion.
/// Tekst podstawowy dla AI: liczby wyłącznie stąd. Bez treningów w okresie — brak podsumowania.
/// </summary>
public static class ClientRecap
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly string[] MonthLocative =
        ["styczniu", "lutym", "marcu", "kwietniu", "maju", "czerwcu", "lipcu", "sierpniu", "wrześniu", "październiku", "listopadzie", "grudniu"];

    public static string MonthName(DateOnly d) => Pl.DateTimeFormat.MonthNames[d.Month - 1];
    public static string InMonth(DateOnly d) => (d.Month == 9 ? "we " : "w ") + MonthLocative[d.Month - 1];

    /// <summary>Który okres pokazać/wysłać danego dnia: w styczniu — poprzedni rok, w pozostałych miesiącach — poprzedni miesiąc.</summary>
    public static (bool IsYear, DateOnly From, DateOnly To) PeriodFor(DateOnly today)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return today.Month == 1
            ? (true, new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year, 1, 1))
            : (false, monthStart.AddMonths(-1), monthStart);
    }

    /// <summary>Do którego dnia miesiąca karta wisi na pulpicie klienta (rok — dłużej).</summary>
    public static bool IsVisible(DateOnly today) => today.Day <= (today.Month == 1 ? 20 : 10);

    public static RecapResult Compute(RecapInput x)
    {
        var periodName = x.IsYear ? x.From.Year.ToString() : MonthName(x.From);
        var title = x.IsYear ? $"Twój rok {x.From.Year}" : $"Twój {periodName}";
        var subtitle = x.IsYear ? "Podsumowanie roku z Twoim trenerem" : $"Podsumowanie: {periodName} {x.From.Year}";
        if (x.TrainingDays == 0) return new(title, subtitle, [], [], [], "", "");

        var when = x.IsYear ? $"W {x.From.Year} roku" : Capitalize(InMonth(x.From));
        var before = x.IsYear ? $"w {x.From.Year - 1}" : InMonth(x.From.AddMonths(-1));
        var facts = new Dictionary<string, string>
        {
            ["okres"] = x.IsYear ? x.From.Year.ToString() : $"{periodName} {x.From.Year}",
            ["treningi"] = x.TrainingDays.ToString(),
        };
        var tiles = new List<RecapTile> { new(x.TrainingDays.ToString(), Plural(x.TrainingDays, "trening", "treningi", "treningów"), "bi-calendar-check") };
        var highlights = new List<string>();
        var parts = new List<string>();

        // Treningi i porównanie z poprzednim okresem.
        var first = $"{when} {x.TrainingDays} {Plural(x.TrainingDays, "trening", "treningi", "treningów")}";
        if (x.IsYear && x.ActiveMonths > 0)
        {
            facts["miesiace_z_treningiem"] = x.ActiveMonths.ToString();
            first += $" w {x.ActiveMonths} {Plural(x.ActiveMonths, "miesiącu", "miesiącach", "miesiącach")}";
        }
        var diff = x.TrainingDays - x.TrainingDaysPrevious;
        if (x.TrainingDaysPrevious > 0)
        {
            facts["treningi_poprzednio"] = x.TrainingDaysPrevious.ToString();
            if (x.IsYear) facts["poprzedni_rok"] = (x.From.Year - 1).ToString();
            if (diff > 0)
            {
                facts["roznica"] = "+" + diff;
                first += $" — o {diff} więcej niż {before}";
                highlights.Add($"+{diff} {Plural(diff, "trening", "treningi", "treningów")} względem poprzedniego {(x.IsYear ? "roku" : "miesiąca")}");
            }
            else if (diff == 0) first += $" — tyle samo co {before}";
        }
        parts.Add(first + ".");

        // Seria tygodni.
        var streak = x.IsYear ? x.BestStreak : x.WeekStreak;
        if (streak >= 2)
        {
            facts[x.IsYear ? "najdluzsza_seria_tygodni" : "tygodnie_z_rzedu"] = streak.ToString();
            tiles.Add(new(streak.ToString(), x.IsYear ? "tyg. najdłuższej serii" : "tyg. z rzędu", "bi-fire"));
            parts.Add(x.IsYear
                ? $"Najdłuższa seria: {streak} {Plural(streak, "tydzień", "tygodnie", "tygodni")} z rzędu z treningiem."
                : $"To już {streak} {Plural(streak, "tydzień", "tygodnie", "tygodni")} z rzędu z treningiem.");
        }

        // Rekordy i postęp.
        if (x.Records > 0)
        {
            facts["rekordy"] = x.Records.ToString();
            tiles.Add(new(x.Records.ToString(), Plural(x.Records, "rekord", "rekordy", "rekordów"), "bi-trophy"));
            if (x.TopRecord is { } rec)
            {
                facts["najlepszy_rekord"] = $"{rec.Exercise}: {Kg(rec.Kg)} kg";
                highlights.Add($"Rekord: {rec.Exercise} — {Kg(rec.Kg)} kg");
                if (!x.IsYear) parts.Add($"Nowy rekord: {rec.Exercise} — {Kg(rec.Kg)} kg.");
            }
        }
        if (x.TopGain is { } gain && gain.ToKg > gain.FromKg)
        {
            facts["najwiekszy_postep"] = $"{gain.Exercise}: {Kg(gain.FromKg)} → {Kg(gain.ToKg)} kg";
            highlights.Add($"{gain.Exercise}: {Kg(gain.FromKg)} → {Kg(gain.ToKg)} kg");
            parts.Add($"Największy postęp: {gain.Exercise}, z {Kg(gain.FromKg)} do {Kg(gain.ToKg)} kg.");
        }
        else if (x.IsYear && x.Records > 0)
            parts.Add($"Pobite rekordy: {x.Records}.");

        // Medale.
        if (x.Medals.Count > 0)
        {
            facts["medale"] = string.Join(", ", x.Medals);
            facts["liczba_medali"] = x.Medals.Count.ToString();
            tiles.Add(new(x.Medals.Count.ToString(), Plural(x.Medals.Count, "medal", "medale", "medali"), "bi-award"));
            parts.Add(x.Medals.Count == 1 ? $"Nowy medal: {x.Medals[0]}." : $"Nowe medale: {string.Join(", ", x.Medals.Take(3))}.");
        }

        parts.Add(x.IsYear ? "Dziękujemy za ten rok — kolejny będzie jeszcze lepszy!" : diff < 0 && x.TrainingDaysPrevious > 0
            ? "Każdy trening się liczy — w nowym miesiącu łatwo wrócić do rytmu."
            : "Tak trzymać!");

        var push = x.IsYear
            ? $"{x.TrainingDays} {Plural(x.TrainingDays, "trening", "treningi", "treningów")} w {x.From.Year} roku — zobacz swoje podsumowanie roku."
            : $"{Capitalize(InMonth(x.From))}: {x.TrainingDays} {Plural(x.TrainingDays, "trening", "treningi", "treningów")}" +
              (x.Records > 0 ? $" i {x.Records} {Plural(x.Records, "rekord", "rekordy", "rekordów")}" : "") + ". Zobacz podsumowanie.";
        return new(title, subtitle, tiles, highlights, facts, string.Join(" ", parts), push);
    }

    /// <summary>Rekordy w okresie: pierwszy raz ciężej niż kiedykolwiek wcześniej w danym ćwiczeniu (cała historia przed dniem).</summary>
    public static (int Count, (int ExerciseId, decimal Kg)? Top) RecordsIn(
        IEnumerable<(int ExerciseId, DateOnly Date, decimal MaxKg)> sessions, DateOnly from, DateOnly to)
    {
        var count = 0;
        (int ExerciseId, decimal Kg, decimal Gain)? top = null;
        foreach (var g in sessions.Where(s => s.MaxKg > 0 && s.Date < to).GroupBy(s => s.ExerciseId))
        {
            decimal? best = null;
            foreach (var s in g.OrderBy(x => x.Date))
            {
                if (best is { } b && s.MaxKg > b && s.Date >= from)
                {
                    count++;
                    var gain = (s.MaxKg - b) / b;
                    if (top is null || s.MaxKg > top.Value.Kg || (s.MaxKg == top.Value.Kg && gain > top.Value.Gain)) top = (g.Key, s.MaxKg, gain);
                }
                best = best is null ? s.MaxKg : Math.Max(best.Value, s.MaxKg);
            }
        }
        return (count, top is { } t ? (t.ExerciseId, t.Kg) : null);
    }

    /// <summary>Największy przyrost w okresie: pierwszy trening z ćwiczeniem w okresie → najlepszy wynik w okresie.</summary>
    public static (int ExerciseId, decimal FromKg, decimal ToKg)? TopGain(
        IEnumerable<(int ExerciseId, DateOnly Date, decimal MaxKg)> sessions, DateOnly from, DateOnly to)
    {
        (int, decimal, decimal)? top = null;
        decimal topDelta = 0;
        foreach (var g in sessions.Where(s => s.MaxKg > 0 && s.Date >= from && s.Date < to).GroupBy(s => s.ExerciseId))
        {
            var list = g.OrderBy(s => s.Date).ToList();
            if (list.Count < 2) continue;
            var start = list[0].MaxKg;
            var max = list.Max(s => s.MaxKg);
            if (max - start > topDelta) { topDelta = max - start; top = (g.Key, start, max); }
        }
        return top;
    }

    private static string Kg(decimal v) => v.ToString("0.##", Pl);
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Pl) + s[1..];

    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
