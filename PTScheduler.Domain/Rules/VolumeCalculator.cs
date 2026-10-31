using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Entities;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Objętość treningowa — czysta matematyka pod wykresy (mocny argument
/// sprzedażowy modułu). Objętość serii = powtórzenia × ciężar [kg];
/// objętość wykonania = suma serii. Agregacja „per partia" przypisuje pełną
/// objętość wykonania każdej z partii pierwotnych ćwiczenia (nie dzieli),
/// bo np. wyciskanie liczy się w całości do klatki.
/// </summary>
public static class VolumeCalculator
{
    /// <summary>Objętość pojedynczej serii = powtórzenia × ciężar.</summary>
    public static decimal SetVolume(int reps, decimal weightKg) => reps * weightKg;

    /// <summary>Objętość jednej serii z logu.</summary>
    public static decimal SetVolume(WorkoutSetLog set) => SetVolume(set.Reps, set.WeightKg);

    /// <summary>Suma objętości serii danego wykonania.</summary>
    public static decimal TotalVolume(IEnumerable<WorkoutSetLog> sets) =>
        sets.Sum(SetVolume);

    /// <summary>
    /// Objętość zagregowana po partii mięśniowej. Dla każdego wykonania bierze
    /// partie pierwotne z CSV ćwiczenia i dopisuje pełną objętość wykonania do
    /// każdej z nich. Wykonania bez rozpoznanych partii są pomijane.
    /// </summary>
    /// <param name="workouts">Wykonania z policzalną objętością i CSV partii pierwotnych.</param>
    public static IReadOnlyDictionary<MuscleGroup, decimal> VolumeByMuscle(
        IEnumerable<(string PrimaryMusclesCsv, decimal Volume)> workouts)
    {
        var totals = new Dictionary<MuscleGroup, decimal>();
        foreach (var (csv, volume) in workouts)
            foreach (var muscle in Muscles.Parse(csv))
                totals[muscle] = totals.TryGetValue(muscle, out var acc) ? acc + volume : volume;
        return totals;
    }
}
