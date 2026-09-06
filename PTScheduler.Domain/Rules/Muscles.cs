using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Parsowanie i prezentacja partii mięśniowych. Na encji ćwiczenia mięśnie
/// trzymamy jako CSV kanonicznych kluczy zgodnych z Free Exercise DB
/// (np. „lower back"), żeby uniknąć mapowania kolekcji enumów w EF. Tu jest
/// jedno miejsce zamiany klucz ↔ <see cref="MuscleGroup"/> i etykieta PL.
/// </summary>
public static class Muscles
{
    // Kanoniczny klucz FED -> enum. Klucze pisane małą literą, spacje jak w FED.
    private static readonly IReadOnlyDictionary<string, MuscleGroup> ByKey =
        new Dictionary<string, MuscleGroup>(StringComparer.OrdinalIgnoreCase)
        {
            ["abdominals"] = MuscleGroup.Abdominals,
            ["abductors"] = MuscleGroup.Abductors,
            ["adductors"] = MuscleGroup.Adductors,
            ["biceps"] = MuscleGroup.Biceps,
            ["calves"] = MuscleGroup.Calves,
            ["chest"] = MuscleGroup.Chest,
            ["forearms"] = MuscleGroup.Forearms,
            ["glutes"] = MuscleGroup.Glutes,
            ["hamstrings"] = MuscleGroup.Hamstrings,
            ["lats"] = MuscleGroup.Lats,
            ["lower back"] = MuscleGroup.LowerBack,
            ["middle back"] = MuscleGroup.MiddleBack,
            ["neck"] = MuscleGroup.Neck,
            ["quadriceps"] = MuscleGroup.Quadriceps,
            ["shoulders"] = MuscleGroup.Shoulders,
            ["traps"] = MuscleGroup.Traps,
            ["triceps"] = MuscleGroup.Triceps
        };

    private static readonly IReadOnlyDictionary<MuscleGroup, string> KeyOf =
        ByKey.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly IReadOnlyDictionary<MuscleGroup, string> LabelPl =
        new Dictionary<MuscleGroup, string>
        {
            [MuscleGroup.Abdominals] = "Brzuch",
            [MuscleGroup.Abductors] = "Odwodziciele",
            [MuscleGroup.Adductors] = "Przywodziciele",
            [MuscleGroup.Biceps] = "Biceps",
            [MuscleGroup.Calves] = "Łydki",
            [MuscleGroup.Chest] = "Klatka piersiowa",
            [MuscleGroup.Forearms] = "Przedramiona",
            [MuscleGroup.Glutes] = "Pośladki",
            [MuscleGroup.Hamstrings] = "Dwugłowe ud (hamstrings)",
            [MuscleGroup.Lats] = "Najszersze grzbietu (lats)",
            [MuscleGroup.LowerBack] = "Dolny odcinek grzbietu",
            [MuscleGroup.MiddleBack] = "Środkowy odcinek grzbietu",
            [MuscleGroup.Neck] = "Kark",
            [MuscleGroup.Quadriceps] = "Czworogłowe ud (quadriceps)",
            [MuscleGroup.Shoulders] = "Barki",
            [MuscleGroup.Traps] = "Kaptury (traps)",
            [MuscleGroup.Triceps] = "Triceps"
        };

    /// <summary>Rozbija CSV kluczy na listę partii, pomijając nieznane/puste.</summary>
    public static IReadOnlyList<MuscleGroup> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        var result = new List<MuscleGroup>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ByKey.TryGetValue(raw, out var m) && !result.Contains(m))
                result.Add(m);
        return result;
    }

    /// <summary>Składa listę partii z powrotem w kanoniczny CSV (do zapisu).</summary>
    public static string ToCsv(IEnumerable<MuscleGroup> muscles) =>
        string.Join(",", muscles.Distinct().Select(m => KeyOf[m]));

    /// <summary>Etykieta PL do UI.</summary>
    public static string Label(MuscleGroup m) => LabelPl[m];
}
