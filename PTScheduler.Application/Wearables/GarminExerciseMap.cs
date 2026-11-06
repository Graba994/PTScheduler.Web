namespace PTScheduler.Application.Wearables;

/// <summary>
/// Dopasowanie naszego ćwiczenia do katalogu Garmina (kategoria + nazwa z Training API, Appendix B), żeby zegarek
/// pokazał właściwe ćwiczenie i liczył powtórzenia. Dopasowanie po słowach nazwy (angielskiej; polską tłumaczymy
/// najczęstszymi słowami). Bez dopasowania krok idzie bez kategorii — z nazwą ćwiczenia w opisie.
/// </summary>
public static class GarminExerciseMap
{
    public sealed record Match(string Category, string Name);

    private sealed record Entry(string Category, string Name, string[] Tokens, string[] CategoryTokens);

    private static readonly Lazy<Entry[]> Entries = new(Load);

    /// <summary>Kategorie dla specjalnego sprzętu — tylko gdy nazwa wprost o nim mówi.</summary>
    private static readonly Dictionary<string, string[]> NicheCategories = new()
    {
        ["BANDED_EXERCISES"] = ["BAND", "BANDED", "GUMA", "GUMĄ"],
        ["SUSPENSION"] = ["SUSPENSION", "TRX"],
        ["SANDBAG"] = ["SANDBAG"],
        ["BATTLE_ROPE"] = ["ROPE", "LINA", "LINY"],
        ["SLED"] = ["SLED", "SANIE"],
        ["SLEDGE_HAMMER"] = ["HAMMER", "MŁOT"],
        ["TIRE"] = ["TIRE", "OPONA"],
    };

    /// <summary>Słowa, które same nie mówią, jakie to ćwiczenie (sprzęt, ustawienie).</summary>
    private static readonly HashSet<string> Modifiers =
    [
        "BARBELL", "DUMBBELL", "KETTLEBELL", "CABLE", "MACHINE", "SMITH", "BAND", "BANDED", "BODY", "WEIGHT", "WEIGHTED",
        "SINGLE", "ARM", "LEG", "ALTERNATING", "SEATED", "STANDING", "LYING", "INCLINE", "DECLINE", "WIDE", "CLOSE", "GRIP",
        "REVERSE", "ONE", "TWO", "SWISS", "BALL", "FULL", "NEUTRAL", "EZ", "BAR", "MEDIUM"
    ];

    /// <summary>Słowa bez znaczenia — pomijane po obu stronach.</summary>
    private static readonly HashSet<string> Stop = ["WITH", "ON", "THE", "AND", "TO", "OF", "A", "IN", "AT", "FOR"];

    /// <summary>Najczęstsze polskie słowa z nazw ćwiczeń → słowa katalogu Garmina.</summary>
    private static readonly Dictionary<string, string[]> Polish = new(StringComparer.OrdinalIgnoreCase)
    {
        ["przysiad"] = ["SQUAT"], ["przysiady"] = ["SQUAT"], ["bułgarski"] = ["SPLIT", "SQUAT"],
        ["wyciskanie"] = ["PRESS"], ["leżąc"] = ["BENCH"], ["ławce"] = ["BENCH"], ["żołnierskie"] = ["SHOULDER"], ["głowę"] = ["SHOULDER"],
        ["martwy"] = ["DEADLIFT"], ["ciąg"] = ["DEADLIFT"], ["rumuński"] = ["ROMANIAN"],
        ["wiosłowanie"] = ["ROW"], ["uginanie"] = ["CURL"], ["biceps"] = ["BICEPS"], ["bicepsów"] = ["BICEPS"],
        ["pompki"] = ["PUSH", "UP"], ["pompka"] = ["PUSH", "UP"], ["podciąganie"] = ["PULL", "UP"], ["deska"] = ["PLANK"], ["plank"] = ["PLANK"],
        ["wykroki"] = ["LUNGE"], ["wypady"] = ["LUNGE"], ["brzuszki"] = ["CRUNCH"], ["spięcia"] = ["CRUNCH"],
        ["biodra"] = ["HIP", "RAISE"], ["bioder"] = ["HIP", "RAISE"], ["hip"] = ["HIP"], ["thrust"] = ["THRUST"],
        ["wspięcia"] = ["CALF", "RAISE"], ["łydki"] = ["CALF"], ["wznosy"] = ["RAISE"], ["bokiem"] = ["LATERAL"],
        ["prostowanie"] = ["EXTENSION"], ["triceps"] = ["TRICEPS"], ["tricepsów"] = ["TRICEPS"], ["francuskie"] = ["TRICEPS", "EXTENSION"],
        ["rozpiętki"] = ["FLYE"], ["szrugsy"] = ["SHRUG"], ["wzruszanie"] = ["SHRUG"], ["ramion"] = ["SHOULDER"],
        ["sztangą"] = ["BARBELL"], ["sztanga"] = ["BARBELL"], ["hantlami"] = ["DUMBBELL"], ["hantlem"] = ["DUMBBELL"], ["hantle"] = ["DUMBBELL"],
        ["kettlebell"] = ["KETTLEBELL"], ["wyciągu"] = ["CABLE"], ["wyciąg"] = ["CABLE"], ["maszynie"] = ["MACHINE"],
        ["nóg"] = ["LEG"], ["nogi"] = ["LEG"], ["unoszenie"] = ["RAISE"], ["wznos"] = ["RAISE"], ["przenoszenie"] = ["CARRY"], ["farmera"] = ["FARMER", "CARRY"],
    };

    public static int Count => Entries.Value.Length;

    /// <summary>Najlepsze dopasowanie albo null, gdy nic sensownego nie pasuje.</summary>
    public static Match? Find(string? nameEn, string? namePl = null)
    {
        var best = Best(Tokens(nameEn));
        if (best is null && !string.IsNullOrWhiteSpace(namePl)) best = Best(PolishTokens(namePl));
        return best;
    }

    private static Match? Best(string[] ours)
    {
        if (ours.Length == 0 || !ours.Any(t => !Modifiers.Contains(t))) return null;
        var set = ours.ToHashSet();
        Entry? best = null;
        double bestScore = 0;
        foreach (var e in Entries.Value)
        {
            var matched = e.Tokens.Count(set.Contains);
            if (matched == 0 || !e.Tokens.Any(t => set.Contains(t) && !Modifiers.Contains(t))) continue;
            // Ruch (przysiad, wyciskanie) waży więcej niż sprzęt i ustawienie (sztanga, siedząc).
            // Brakujący u nas wariant (np. „wąski chwyt”) to inne ćwiczenie — kosztuje więcej, niż daje dopasowany sprzęt.
            var score = e.Tokens.Sum(t => set.Contains(t) ? (Modifiers.Contains(t) ? 1.0 : 3.0) : (Modifiers.Contains(t) ? -1.5 : -2.0))
                        - set.Count(t => !Modifiers.Contains(t) && !e.Tokens.Contains(t)) * 0.5;
            if (e.CategoryTokens.All(set.Contains)) score += 3;
            if (NicheCategories.TryGetValue(e.Category, out var words) && !words.Any(set.Contains)) score -= 5;
            if (score > bestScore || (score == bestScore && best is not null && e.Tokens.Length < best.Tokens.Length))
            {
                best = e;
                bestScore = score;
            }
        }
        return best is null || bestScore < 2 ? null : new Match(best.Category, best.Name);
    }

    /// <summary>„Barbell Full Squats” → [BARBELL, FULL, SQUAT]; „Push-up” → [PUSH, UP].</summary>
    public static string[] Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var chars = text.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(Split).Select(Singular).Where(t => !Stop.Contains(t)).ToArray();
    }

    /// <summary>Zlepki z nazw: PUSHUP → PUSH UP, PULLUP → PULL UP, SITUP → SIT UP, CHINUP → CHIN UP.</summary>
    private static IEnumerable<string> Split(string t) => t switch
    {
        "PUSHUP" or "PUSHUPS" => ["PUSH", "UP"],
        "PULLUP" or "PULLUPS" => ["PULL", "UP"],
        "SITUP" or "SITUPS" => ["SIT", "UP"],
        "CHINUP" or "CHINUPS" => ["CHIN", "UP"],
        "STEPUP" or "STEPUPS" => ["STEP", "UP"],
        "FLY" or "FLIES" or "FLYES" => ["FLYE"],
        "UPS" => ["UP"],
        "DUMBBELLS" => ["DUMBBELL"],
        "BICEP" => ["BICEPS"],
        "TRICEP" => ["TRICEPS"],
        "BRIDGE" => ["BRIDGE", "HIP", "RAISE"],
        _ => [t]
    };

    private static string Singular(string t) =>
        t.Length > 4 && t.EndsWith('S') && !t.EndsWith("SS") && t is not ("BICEPS" or "TRICEPS" or "ABS" or "GLUTES") ? t[..^1] : t;

    private static string[] PolishTokens(string text) =>
        text.ToLowerInvariant().Split([' ', '-', ',', '(', ')', '/'], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(w => Polish.TryGetValue(w, out var en) ? en : [])
            .ToArray();

    private static Entry[] Load()
    {
        using var stream = typeof(GarminExerciseMap).Assembly.GetManifestResourceStream("GarminStrengthExercises.txt")
            ?? throw new InvalidOperationException("Brak zasobu GarminStrengthExercises.txt");
        using var reader = new StreamReader(stream);
        var list = new List<Entry>();
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            list.Add(new Entry(parts[0], parts[1], Tokens(parts[1].Replace('_', ' ')), Tokens(parts[0].Replace('_', ' '))));
        }
        return [.. list];
    }
}
