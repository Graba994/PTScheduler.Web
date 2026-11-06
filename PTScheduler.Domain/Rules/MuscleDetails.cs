using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Szczegółowe partie mięśni: klucze do zapisu (CSV), etykiety PL, przypisanie do partii ogólnej i wnioskowanie
/// z partii ogólnych + nazwy ćwiczenia (katalog Free Exercise DB ma tylko 17 partii — „barki” bez podziału na aktony).
/// Własny wybór trenera zapisany w ćwiczeniu ma pierwszeństwo przed wnioskowaniem.
/// </summary>
public static class MuscleDetails
{
    private static readonly (MuscleDetail Detail, string Key, string Label, MuscleGroup Group)[] Table =
    [
        (MuscleDetail.FrontDelts, "front delts", "Barki — przód", MuscleGroup.Shoulders),
        (MuscleDetail.SideDelts, "side delts", "Barki — bok", MuscleGroup.Shoulders),
        (MuscleDetail.RearDelts, "rear delts", "Barki — tył", MuscleGroup.Shoulders),
        (MuscleDetail.UpperChest, "upper chest", "Klatka — góra", MuscleGroup.Chest),
        (MuscleDetail.LowerChest, "lower chest", "Klatka — środek i dół", MuscleGroup.Chest),
        (MuscleDetail.Biceps, "biceps", "Biceps", MuscleGroup.Biceps),
        (MuscleDetail.Triceps, "triceps", "Triceps", MuscleGroup.Triceps),
        (MuscleDetail.Forearms, "forearms", "Przedramiona", MuscleGroup.Forearms),
        (MuscleDetail.Abs, "abs", "Brzuch — prosty", MuscleGroup.Abdominals),
        (MuscleDetail.Obliques, "obliques", "Brzuch — skośne", MuscleGroup.Abdominals),
        (MuscleDetail.UpperTraps, "upper traps", "Kaptury — góra", MuscleGroup.Traps),
        (MuscleDetail.MidTraps, "mid traps", "Kaptury — środek i dół", MuscleGroup.Traps),
        (MuscleDetail.Rhomboids, "rhomboids", "Równoległoboczne", MuscleGroup.MiddleBack),
        (MuscleDetail.Scapular, "scapular", "Mięśnie łopatki (stożek rotatorów)", MuscleGroup.MiddleBack),
        (MuscleDetail.Lats, "lats", "Najszersze grzbietu", MuscleGroup.Lats),
        (MuscleDetail.LowerBack, "lower back", "Prostowniki grzbietu", MuscleGroup.LowerBack),
        (MuscleDetail.Glutes, "glutes", "Pośladek wielki", MuscleGroup.Glutes),
        (MuscleDetail.GluteMedius, "glute medius", "Pośladek średni i odwodziciele", MuscleGroup.Abductors),
        (MuscleDetail.Adductors, "adductors", "Przywodziciele", MuscleGroup.Adductors),
        (MuscleDetail.Quads, "quads", "Czworogłowy uda", MuscleGroup.Quadriceps),
        (MuscleDetail.Hamstrings, "hamstrings", "Dwugłowy uda", MuscleGroup.Hamstrings),
        (MuscleDetail.Calves, "calves", "Łydki", MuscleGroup.Calves),
        (MuscleDetail.Tibialis, "tibialis", "Piszczelowy przedni", MuscleGroup.Calves),
        (MuscleDetail.Neck, "neck", "Szyja i kark", MuscleGroup.Neck),
    ];

    private static readonly Dictionary<string, MuscleDetail> ByKey =
        Table.ToDictionary(t => t.Key, t => t.Detail, StringComparer.OrdinalIgnoreCase);

    public static string Key(MuscleDetail d) => Table[(int)d].Key;
    public static string Label(MuscleDetail d) => Table[(int)d].Label;
    public static MuscleGroup Group(MuscleDetail d) => Table[(int)d].Group;

    public static IReadOnlyList<MuscleDetail> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        var result = new List<MuscleDetail>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ByKey.TryGetValue(raw, out var d) && !result.Contains(d)) result.Add(d);
        return result;
    }

    public static string ToCsv(IEnumerable<MuscleDetail> details) => string.Join(",", details.Distinct().Select(Key));

    /// <summary>Partie ogólne z wybranych szczegółowych (do objętości, filtrów i zapisu zgodnego z katalogiem).</summary>
    public static (List<MuscleGroup> Primary, List<MuscleGroup> Secondary) ToGroups(IEnumerable<MuscleDetail> primary, IEnumerable<MuscleDetail> secondary)
    {
        var p = primary.Select(Group).Distinct().ToList();
        var s = secondary.Select(Group).Distinct().Where(g => !p.Contains(g)).ToList();
        return (p, s);
    }

    /// <summary>
    /// Szczegółowe partie ćwiczenia: zapisany wybór trenera, a gdy go nie ma — wnioskowanie z partii ogólnych i nazwy.
    /// </summary>
    public static (IReadOnlyList<MuscleDetail> Primary, IReadOnlyList<MuscleDetail> Secondary) Resolve(
        string? primaryDetailCsv, string? secondaryDetailCsv, string? primaryCsv, string? secondaryCsv, string? nameEn)
    {
        var storedPrimary = Parse(primaryDetailCsv);
        var storedSecondary = Parse(secondaryDetailCsv);
        if (storedPrimary.Count + storedSecondary.Count > 0) return (storedPrimary, storedSecondary.Where(d => !storedPrimary.Contains(d)).ToList());
        return Infer(Muscles.Parse(primaryCsv), Muscles.Parse(secondaryCsv), nameEn);
    }

    public static (IReadOnlyList<MuscleDetail> Primary, IReadOnlyList<MuscleDetail> Secondary) Infer(
        IReadOnlyCollection<MuscleGroup> primary, IReadOnlyCollection<MuscleGroup> secondary, string? nameEn)
    {
        var name = (nameEn ?? "").ToLowerInvariant().Replace('-', ' ');
        var p = primary.SelectMany(g => Expand(g, true, name, primary)).Distinct().ToList();
        var s = secondary.SelectMany(g => Expand(g, false, name, primary)).Distinct().Where(d => !p.Contains(d)).ToList();
        return (p, s);
    }

    private static bool Has(string name, params string[] words) => words.Any(name.Contains);

    private static IEnumerable<MuscleDetail> Expand(MuscleGroup g, bool isPrimary, string name, IReadOnlyCollection<MuscleGroup> primaryGroups) => g switch
    {
        MuscleGroup.Shoulders => Shoulders(isPrimary, name, primaryGroups),
        MuscleGroup.Chest => Has(name, "incline") ? [MuscleDetail.UpperChest]
            : Has(name, "decline", "dip") ? [MuscleDetail.LowerChest]
            : [MuscleDetail.UpperChest, MuscleDetail.LowerChest],
        MuscleGroup.Abdominals => Has(name, "oblique", "twist", "russian", "side bend", "side plank", "woodchop", "wood chop", "windmill", "rotation", "side jackknife", "saxon", "pallof")
            ? (Has(name, "bicycle", "crunch") ? [MuscleDetail.Abs, MuscleDetail.Obliques] : [MuscleDetail.Obliques])
            : Has(name, "bicycle", "air bike") ? [MuscleDetail.Abs, MuscleDetail.Obliques]
            : [MuscleDetail.Abs],
        MuscleGroup.Traps => primaryGroups.Contains(MuscleGroup.MiddleBack) && !Has(name, "shrug") ? [MuscleDetail.MidTraps] : [MuscleDetail.UpperTraps],
        MuscleGroup.MiddleBack => Has(name, "face pull", "external rotation", "internal rotation", "rotator", "cuban")
            ? [MuscleDetail.Scapular]
            : isPrimary ? [MuscleDetail.Rhomboids, MuscleDetail.MidTraps] : [MuscleDetail.Rhomboids],
        MuscleGroup.Calves => Has(name, "tibialis", "toe raise", "dorsiflex", "reverse calf") ? [MuscleDetail.Tibialis] : [MuscleDetail.Calves],
        MuscleGroup.Abductors => [MuscleDetail.GluteMedius],
        MuscleGroup.Adductors => [MuscleDetail.Adductors],
        MuscleGroup.Glutes => [MuscleDetail.Glutes],
        MuscleGroup.Quadriceps => [MuscleDetail.Quads],
        MuscleGroup.Hamstrings => [MuscleDetail.Hamstrings],
        MuscleGroup.Lats => [MuscleDetail.Lats],
        MuscleGroup.LowerBack => [MuscleDetail.LowerBack],
        MuscleGroup.Biceps => [MuscleDetail.Biceps],
        MuscleGroup.Triceps => [MuscleDetail.Triceps],
        MuscleGroup.Forearms => [MuscleDetail.Forearms],
        MuscleGroup.Neck => [MuscleDetail.Neck],
        _ => []
    };

    private static IEnumerable<MuscleDetail> Shoulders(bool isPrimary, string name, IReadOnlyCollection<MuscleGroup> primaryGroups)
    {
        if (Has(name, "rear", "reverse fly", "reverse flye", "face pull", "bent over lateral", "bent over dumbbell rear", "reverse pec deck", "reverse machine fly"))
            return [MuscleDetail.RearDelts];
        if (Has(name, "front raise", "front plate", "front dumbbell raise", "front cable raise", "front incline dumbbell raise"))
            return [MuscleDetail.FrontDelts];
        if (Has(name, "lateral raise", "side lateral", "upright row", "lateral", "y raise", "lu raise"))
            return [MuscleDetail.SideDelts];
        if (Has(name, "press", "push press", "jerk", "handstand", "pike push"))
        {
            // Wyciskanie nad głowę — przód i bok; wyciskanie na klatkę (klatka główna) — tylko przód.
            if (primaryGroups.Contains(MuscleGroup.Chest) || Has(name, "bench", "floor press", "chest press"))
                return [MuscleDetail.FrontDelts];
            return [MuscleDetail.FrontDelts, MuscleDetail.SideDelts];
        }
        // Wiosłowania i przyciągania angażują tył barków, wyciskania i pompki — przód.
        if (primaryGroups.Contains(MuscleGroup.Lats) || primaryGroups.Contains(MuscleGroup.MiddleBack) || Has(name, "row", "pull"))
            return [MuscleDetail.RearDelts];
        if (primaryGroups.Contains(MuscleGroup.Chest) || primaryGroups.Contains(MuscleGroup.Triceps) || Has(name, "push up", "pushup", "dip"))
            return [MuscleDetail.FrontDelts];
        return isPrimary ? [MuscleDetail.FrontDelts, MuscleDetail.SideDelts, MuscleDetail.RearDelts] : [MuscleDetail.FrontDelts];
    }
}
