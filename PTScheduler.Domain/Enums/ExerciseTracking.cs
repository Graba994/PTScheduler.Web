namespace PTScheduler.Domain.Enums;

/// <summary>
/// Co mierzymy w serii ćwiczenia — od tego zależą pola w planie i podczas treningu
/// (bieg nie ma ciężaru ani powtórzeń, deska ma tylko czas).
/// </summary>
public enum ExerciseTracking
{
    /// <summary>Ciężar i powtórzenia (wyciskanie, przysiad ze sztangą).</summary>
    WeightReps = 0,
    /// <summary>Same powtórzenia — masa ciała (pompki, brzuszki, podciąganie).</summary>
    Reps = 1,
    /// <summary>Czas (deska, izometria, rozciąganie, skakanka).</summary>
    Time = 2,
    /// <summary>Dystans i czas (bieg, rower, wioślarz).</summary>
    DistanceTime = 3,
    /// <summary>Ciężar i dystans (spacer farmera, ciągnięcie sań).</summary>
    WeightDistance = 4
}
