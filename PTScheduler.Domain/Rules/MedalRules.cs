namespace PTScheduler.Domain.Rules;

public enum MedalTier { Bronze, Silver, Gold }

/// <summary>Grafika w środku medalu: liczba (np. „50”) albo ikona.</summary>
public enum MedalGlyph { Number, Bolt, Flame, Star, Calendar, Heart, Dumbbell }

/// <summary>Statystyki klienta, z których liczymy medale (cała historia).</summary>
public sealed record MedalStats(int Trainings, int BestWeekStreak, int Records, int MonthsTogether, int Surveys, int AppWorkouts);

/// <summary>
/// Medale klienta: kamienie milowe treningów, serie tygodni, rekordy, staż ze trenerem, ankiety i treningi z planu.
/// Zdobyty medal zostaje na zawsze (zapis w ClientMedal), więc liczymy od całej historii.
/// </summary>
public static class MedalRules
{
    public sealed record Medal(string Key, string Name, string Description, MedalTier Tier, MedalGlyph Glyph, string? Label, Func<MedalStats, int> Value, int Target);

    public static readonly IReadOnlyList<Medal> All =
    [
        new("treningi_1", "Pierwszy krok", "Pierwszy trening za Tobą", MedalTier.Bronze, MedalGlyph.Bolt, null, s => s.Trainings, 1),
        new("treningi_10", "10 treningów", "Dziesięć treningów — nawyk się rodzi", MedalTier.Bronze, MedalGlyph.Number, "10", s => s.Trainings, 10),
        new("treningi_25", "25 treningów", "Ćwierć setki treningów", MedalTier.Silver, MedalGlyph.Number, "25", s => s.Trainings, 25),
        new("treningi_50", "50 treningów", "Pięćdziesiąt treningów — to już styl życia", MedalTier.Gold, MedalGlyph.Number, "50", s => s.Trainings, 50),
        new("treningi_100", "100 treningów", "Setka! Niewielu tu dociera", MedalTier.Gold, MedalGlyph.Number, "100", s => s.Trainings, 100),
        new("treningi_200", "200 treningów", "Dwieście treningów — legenda studia", MedalTier.Gold, MedalGlyph.Number, "200", s => s.Trainings, 200),
        new("seria_4", "Miesiąc bez przerwy", "Trening w 4 kolejnych tygodniach", MedalTier.Bronze, MedalGlyph.Flame, null, s => s.BestWeekStreak, 4),
        new("seria_8", "Dwa miesiące w rytmie", "Trening w 8 kolejnych tygodniach", MedalTier.Silver, MedalGlyph.Flame, null, s => s.BestWeekStreak, 8),
        new("seria_12", "Kwartał regularności", "Trening w 12 kolejnych tygodniach", MedalTier.Gold, MedalGlyph.Flame, null, s => s.BestWeekStreak, 12),
        new("rekord_1", "Pierwszy rekord", "Pierwszy raz ciężej niż kiedykolwiek", MedalTier.Bronze, MedalGlyph.Star, null, s => s.Records, 1),
        new("rekord_10", "10 rekordów", "Dziesięć razy pobity własny rekord", MedalTier.Silver, MedalGlyph.Star, null, s => s.Records, 10),
        new("rekord_25", "25 rekordów", "Ćwierć setki rekordów — siła rośnie", MedalTier.Gold, MedalGlyph.Star, null, s => s.Records, 25),
        new("staz_6", "Pół roku razem", "Sześć miesięcy treningów", MedalTier.Silver, MedalGlyph.Calendar, null, s => s.MonthsTogether, 6),
        new("staz_12", "Rok razem", "Rok wspólnej pracy", MedalTier.Gold, MedalGlyph.Calendar, null, s => s.MonthsTogether, 12),
        new("ankiety_10", "Szczery feedback", "Dziesięć ankiet po treningu — dzięki nim plan jest lepszy", MedalTier.Bronze, MedalGlyph.Heart, null, s => s.Surveys, 10),
        new("plan_10", "Samodzielność", "Dziesięć treningów z planu w aplikacji", MedalTier.Bronze, MedalGlyph.Dumbbell, null, s => s.AppWorkouts, 10),
        new("plan_30", "Plan w małym palcu", "Trzydzieści treningów z planu w aplikacji", MedalTier.Silver, MedalGlyph.Dumbbell, null, s => s.AppWorkouts, 30),
    ];

    public static Medal? Find(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>Klucze medali zdobytych przy tych statystykach.</summary>
    public static List<string> Earned(MedalStats stats) => All.Where(m => m.Value(stats) >= m.Target).Select(m => m.Key).ToList();

    /// <summary>Postęp do medalu (0–1) — do paska przy medalach jeszcze niezdobytych.</summary>
    public static double Progress(Medal medal, MedalStats stats) => Math.Clamp((double)medal.Value(stats) / medal.Target, 0, 1);

    /// <summary>Najdłuższa seria kolejnych tygodni (od poniedziałku) z co najmniej jednym treningiem.</summary>
    public static int BestWeekStreak(IEnumerable<DateOnly> days)
    {
        static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        var weeks = days.Select(Monday).Distinct().OrderBy(d => d).ToList();
        var best = 0;
        var run = 0;
        for (var i = 0; i < weeks.Count; i++)
        {
            run = i > 0 && weeks[i].DayNumber - weeks[i - 1].DayNumber == 7 ? run + 1 : 1;
            best = Math.Max(best, run);
        }
        return best;
    }

    /// <summary>Ile razy klient pobił swój rekord ciężaru w ćwiczeniu (pierwszy raz z ćwiczeniem to nie rekord).</summary>
    public static int CountRecords(IEnumerable<(int ExerciseId, DateOnly Date, decimal MaxKg)> sessions)
    {
        var records = 0;
        foreach (var g in sessions.Where(s => s.MaxKg > 0).GroupBy(s => s.ExerciseId))
        {
            decimal? best = null;
            foreach (var s in g.OrderBy(x => x.Date))
            {
                if (best is { } b && s.MaxKg > b) records++;
                best = best is null ? s.MaxKg : Math.Max(best.Value, s.MaxKg);
            }
        }
        return records;
    }
}
