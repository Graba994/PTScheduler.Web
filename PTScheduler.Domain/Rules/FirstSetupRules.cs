using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Konfigurator „Pierwsze ustawienia” (/witaj): gotowe układy tygodnia, sprawdzenie godzin pracy,
/// najbliższe wolne terminy do podglądu i zasady odwołań opisane językiem klienta.
/// </summary>
public static class FirstSetupRules
{
    /// <summary>Jedno okno pracy w tygodniu (np. poniedziałek 8:00–20:00).</summary>
    public sealed record Window(DayOfWeek Day, TimeOnly Start, TimeOnly End);

    public sealed record Preset(string Key, string Name, string Hint, string Icon, IReadOnlyList<Window> Windows);

    /// <summary>Kolejność dni jak w polskim kalendarzu: od poniedziałku.</summary>
    public static readonly DayOfWeek[] Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private static readonly DayOfWeek[] Weekdays = Week[..5];

    public static readonly IReadOnlyList<Preset> Presets =
    [
        new("dzien", "Cały dzień", "pon.–pt. 8:00–20:00", "bi-sun", Days(Weekdays, 8, 20)),
        new("rano-wieczor", "Rano i wieczorem", "pon.–pt. 6–10 i 16–21", "bi-sunrise", [.. Days(Weekdays, 6, 10), .. Days(Weekdays, 16, 21)]),
        new("popoludnia", "Popołudnia", "pon.–pt. 14:00–21:00", "bi-sunset", Days(Weekdays, 14, 21)),
        new("z-sobota", "Z sobotą", "pon.–pt. 8–20, sob. 9–14", "bi-calendar-week", [.. Days(Weekdays, 8, 20), new(DayOfWeek.Saturday, new(9, 0), new(14, 0))]),
    ];

    private static List<Window> Days(IEnumerable<DayOfWeek> days, int from, int to) =>
        days.Select(d => new Window(d, new TimeOnly(from, 0), new TimeOnly(to, 0))).ToList();

    /// <summary>Który gotowy układ odpowiada tym godzinom (null — własny).</summary>
    public static string? MatchingPreset(IReadOnlyCollection<Window> windows) =>
        Presets.FirstOrDefault(p => p.Windows.Count == windows.Count && p.Windows.All(windows.Contains))?.Key;

    public static double TotalHours(IEnumerable<Window> windows) =>
        windows.Where(w => w.End > w.Start).Sum(w => (w.End - w.Start).TotalHours);

    public static int WorkDays(IEnumerable<Window> windows) => windows.Select(w => w.Day).Distinct().Count();

    /// <summary>Opis błędu albo null, gdy godziny są w porządku.</summary>
    public static string? Problem(IEnumerable<Window> windows)
    {
        foreach (var day in windows.GroupBy(w => w.Day))
        {
            var ordered = day.OrderBy(w => w.Start).ToList();
            if (ordered.FirstOrDefault(w => w.End <= w.Start) is not null)
                return $"{DayName(day.Key)}: koniec musi być po początku.";
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].Start < ordered[i - 1].End)
                    return $"{DayName(day.Key)}: godziny nachodzą na siebie.";
        }
        return null;
    }

    /// <summary>
    /// Najbliższe terminy, które klient zobaczy w aplikacji (od jutra, co godzinę w oknach pracy) — do podglądu
    /// w konfiguratorze. Prawdziwe wolne terminy liczy grafik (z wizytami i przerwami).
    /// </summary>
    public static IReadOnlyList<DateTime> NextSlots(IReadOnlyCollection<Window> windows, DateTime now, int durationMinutes, int count)
    {
        var slots = new List<DateTime>();
        if (durationMinutes <= 0 || count <= 0) return slots;
        for (var d = 1; d <= 14 && slots.Count < count; d++)
        {
            var date = now.Date.AddDays(d);
            foreach (var w in windows.Where(w => w.Day == date.DayOfWeek && w.End > w.Start).OrderBy(w => w.Start))
            {
                for (var t = w.Start; t.AddMinutes(durationMinutes) <= w.End && t >= w.Start && slots.Count < count; t = t.AddHours(1))
                {
                    slots.Add(date.Add(t.ToTimeSpan()));
                    if (t.Hour == 23) break;
                }
            }
        }
        return slots;
    }

    /// <summary>Zasady odwołań tak, jak przeczyta je klient (podgląd w konfiguratorze).</summary>
    public static string ClientCancellationText(int windowHours, LateCancellationPolicy late, bool noShowCharges)
    {
        if (late == LateCancellationPolicy.Refund)
            return "Trening możesz odwołać w każdej chwili, bez kosztów.";
        var hours = windowHours <= 0 ? "w każdej chwili" : $"do {windowHours} {Hours(windowHours)} przed treningiem";
        var later = late == LateCancellationPolicy.ChargeSession
            ? "Później trening przepada."
            : "Później napisz do trenera.";
        var noShow = noShowCharges ? " Nieobecność bez odwołania = trening przepada." : "";
        return $"Trening odwołasz bez kosztów {hours}. {later}{noShow}";
    }

    /// <summary>
    /// Komunikat o limicie klientów w kroku „pierwszy klient”. Limit planu liczy wszystkich klientów studia, a trener
    /// widzi tylko swoich — dlatego trenerowi mówimy wprost, że liczy się całe studio. Null, gdy miejsca jest dość.
    /// </summary>
    public static (string Text, bool Reached)? ClientLimitNote(int studioClients, int maxClients, int ownClients, bool isOwner)
    {
        if (maxClients == int.MaxValue) return null;
        var left = maxClients - studioClients;
        if (left > 3) return null;
        var plan = isOwner
            ? $"Twój plan obejmuje {maxClients} {Clients(maxClients)}"
            : $"Plan studia obejmuje {maxClients} {Clients(maxClients)} — liczą się klienci wszystkich trenerów (Twoich: {ownClients})";
        if (left <= 0)
            return ($"{plan} i wszystkie miejsca są zajęte. " + (isOwner ? "Kolejne osoby dodasz po zmianie planu." : "Kolejne osoby dodasz, gdy właściciel studia zmieni plan."), true);
        return ($"{plan}. {Plural(left, "Zostało", "Zostały", "Zostało")} {left} {Plural(left, "miejsce", "miejsca", "miejsc")}.", false);
    }

    public static string DayName(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Poniedziałek", DayOfWeek.Tuesday => "Wtorek", DayOfWeek.Wednesday => "Środa",
        DayOfWeek.Thursday => "Czwartek", DayOfWeek.Friday => "Piątek", DayOfWeek.Saturday => "Sobota", _ => "Niedziela"
    };

    public static string DayShort(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "pon.", DayOfWeek.Tuesday => "wt.", DayOfWeek.Wednesday => "śr.",
        DayOfWeek.Thursday => "czw.", DayOfWeek.Friday => "pt.", DayOfWeek.Saturday => "sob.", _ => "niedz."
    };

    private static string Hours(int n) =>
        n == 1 ? "godziny" : "godzin";

    private static string Clients(int n) => Plural(n, "klienta", "klientów", "klientów");

    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14) ? few : many;
}
