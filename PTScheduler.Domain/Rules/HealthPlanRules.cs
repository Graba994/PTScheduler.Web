using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Zestawia to, co klient napisał w ankiecie zdrowotnej (urazy, dolegliwości, ciąża, PAR-Q) i w ankietach
/// po treningu (miejsce bólu), z ćwiczeniami w planie: „ból kolana” → przysiady i wykroki w planie.
/// To podpowiedź do sprawdzenia, nie diagnoza — wskazówki mówią „sprawdź”, „dopytaj”, „rozważ zamiennik”.
/// </summary>
public static class HealthPlanRules
{
    public sealed record Note(string Source, string Text);

    public sealed record PlanExercise(string Name, string PrimaryMuscles, string SecondaryMuscles, ExerciseCategory Category);

    public sealed record Flag(string Area, string Source, string Mention, List<string> Exercises, string Advice);

    private sealed record Area(string Label, string[] Keywords, string[] ExerciseWords, string[] PrimaryMuscles, string Advice, ExerciseCategory[]? Categories = null);

    private static readonly Area[] Areas =
    [
        new("Kolano", ["kolan", "łąkot", "rzepk", "więzadł krzyż", "acl"],
            ["przysiad", "wykrok", "wypad", "zakrok", "skok", "suwnic", "prasa", "leg press", "lunge", "squat", "jump", "step", "pistolet", "bułgar", "prostowanie nóg", "leg extension"],
            ["quadriceps"], "Sprawdź, czy nadal dokucza. Kontroluj zakres i tor kolana; przy bólu rozważ zamiennik (np. mostek, hip thrust, mniejszy zakres).",
            [ExerciseCategory.Plyometrics]),
        new("Kręgosłup / dolny odcinek pleców", ["lędźw", "krzyża", "kręgosłup", "dysk", "przepuklin", "rwa ", "rwa,", "ból pleców", "plecy", "kulsz"],
            ["martwy", "deadlift", "skłon", "good morning", "przysiad ze sztangą", "wiosłowanie w opadzie", "wznosy tułowia", "hyperextension", "rumuński", "przenoszenie"],
            ["lower back"], "Dopytaj o aktualne dolegliwości. Postaw na neutralny kręgosłup i lżejsze obciążenie osiowe; rozważ warianty z podparciem.",
            [ExerciseCategory.Strongman, ExerciseCategory.OlympicWeightlifting]),
        new("Bark", ["bark", "ramię", "ramien", "ramion", "rotator", "obojczyk", "stożek"],
            ["nad głowę", "nad głowe", "overhead", "żołnierskie", "military", "arnold", "podciąganie", "poręcz", "dips", "pull-up", "wyciskanie", "rwanie", "podrzut", "unoszenie ramion"],
            ["shoulders"], "Sprawdź zakres ruchu w barku bez bólu; przy dolegliwościach ogranicz ćwiczenia nad głową i rozważ neutralny chwyt."),
        new("Łokieć / nadgarstek", ["łok", "nadgarst", "tenisist", "golfist", "cieśń", "ciesn"],
            ["pompk", "push-up", "uginanie", "curl", "francuskie", "prostowanie ramion", "wyciskanie", "podpór", "deska", "plank"],
            ["forearms"], "Dopytaj o ból przy chwycie i podporach; rozważ neutralny chwyt, uchwyty do pompek albo mniejsze obciążenie."),
        new("Kostka / stopa", ["kostk", "stopa", "stopy", "stóp", "stopie", "achilles", "achillesa", "skokow", "pięt", "piet", "rozcięgn"],
            ["skok", "bieg", "wspięcia", "calf", "jump", "burpee", "skakank", "box", "sprint", "pajacyk"],
            ["calves"], "Sprawdź stabilność kostki; przy dolegliwościach zamień skoki i bieg na ćwiczenia bez uderzeń.",
            [ExerciseCategory.Plyometrics]),
        new("Biodro", ["biodr", "pachwin", "lędźwiowo-miedn"],
            ["przysiad", "wykrok", "martwy", "hip thrust", "odwodzenie", "przywodzenie", "mostek", "squat", "lunge"],
            ["adductors", "abductors"], "Dopytaj, w jakim ruchu pojawia się ból; dopasuj głębokość przysiadu i rozstaw stóp."),
        new("Szyja", ["szyj", "kark", "odcinek szyjny"],
            ["szrug", "shrug", "przysiad ze sztangą", "wiosłowanie pionowe", "upright"],
            ["neck", "traps"], "Unikaj obciążania szyi; przy przysiadzie rozważ sztangę z przodu albo hantle."),
    ];

    private static readonly Area Pregnancy = new("Ciąża / po porodzie", [],
        ["brzuszk", "crunch", "leżąc na plecach", "na plecach", "deska", "plank", "unoszenie nóg", "scyzoryk", "skok", "burpee"],
        ["abdominals"], "Dopasuj ćwiczenia do etapu ciąży lub połogu i zaleceń lekarza — zwłaszcza leżenie na plecach, mocne napięcie brzucha i skoki.",
        [ExerciseCategory.Plyometrics]);

    private static readonly Area Heart = new("Serce / ciśnienie (PAR-Q)", [],
        ["martwy", "przysiad ze sztangą", "deadlift", "burpee", "sprint", "interwał"],
        [], "Kontroluj intensywność i oddech (bez wstrzymywania przy wysiłku); przy niepokojących objawach przerwij trening.",
        [ExerciseCategory.Strongman, ExerciseCategory.OlympicWeightlifting, ExerciseCategory.Powerlifting, ExerciseCategory.Plyometrics]);

    /// <param name="notes">Wolne odpowiedzi (urazy, inne informacje, miejsce bólu po treningu).</param>
    /// <param name="pregnancy">„Tak” w pytaniu o ciążę lub okres po porodzie.</param>
    /// <param name="heart">„Tak” w PAR-Q o chorobie serca lub nadciśnieniu.</param>
    public static List<Flag> Check(IEnumerable<Note> notes, bool pregnancy, bool heart, IReadOnlyList<PlanExercise> plan)
    {
        var flags = new List<Flag>();
        if (plan.Count == 0) return flags;

        foreach (var note in notes.Where(n => !string.IsNullOrWhiteSpace(n.Text)))
        {
            var text = note.Text.ToLowerInvariant();
            foreach (var area in Areas.Where(a => a.Keywords.Any(text.Contains)))
            {
                if (flags.Any(f => f.Area == area.Label)) continue;
                var matches = Matching(area, plan);
                if (matches.Count > 0)
                    flags.Add(new(area.Label, note.Source, Short(note.Text), matches, area.Advice));
            }
        }
        if (pregnancy && Matching(Pregnancy, plan) is { Count: > 0 } p)
            flags.Add(new(Pregnancy.Label, "Ankieta zdrowotna", "ciąża lub okres po porodzie", p, Pregnancy.Advice));
        if (heart && Matching(Heart, plan) is { Count: > 0 } h)
            flags.Add(new(Heart.Label, "Ankieta zdrowotna", "choroba serca lub nadciśnienie", h, Heart.Advice));
        return flags;
    }

    /// <summary>
    /// Czy pojedyncze ćwiczenie koliduje z notatkami zdrowotnymi — dla generatora planu. Zwraca obszar (np. „Kolano”)
    /// albo null. Te same słowa kluczowe co w <see cref="Check"/>.
    /// </summary>
    public static string? Conflict(IReadOnlyCollection<Note> notes, bool pregnancy, bool heart, PlanExercise exercise)
    {
        var one = new[] { exercise };
        foreach (var note in notes.Where(n => !string.IsNullOrWhiteSpace(n.Text)))
        {
            var text = note.Text.ToLowerInvariant();
            foreach (var area in Areas.Where(a => a.Keywords.Any(text.Contains)))
                if (Matching(area, one).Count > 0) return area.Label;
        }
        if (pregnancy && Matching(Pregnancy, one).Count > 0) return Pregnancy.Label;
        if (heart && Matching(Heart, one).Count > 0) return Heart.Label;
        return null;
    }

    private static List<string> Matching(Area area, IReadOnlyList<PlanExercise> plan) =>
        plan.Where(e =>
            {
                var name = e.Name.ToLowerInvariant();
                return area.ExerciseWords.Any(name.Contains)
                    || Split(e.PrimaryMuscles).Intersect(area.PrimaryMuscles, StringComparer.OrdinalIgnoreCase).Any()
                    || (area.Categories?.Contains(e.Category) ?? false);
            })
            .Select(e => e.Name).Distinct().Take(8).ToList();

    private static IEnumerable<string> Split(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Short(string text)
    {
        var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= 120 ? t : t[..120] + "…";
    }
}
