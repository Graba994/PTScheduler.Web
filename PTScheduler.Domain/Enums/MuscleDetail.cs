namespace PTScheduler.Domain.Enums;

/// <summary>
/// Szczegółowa partia mięśniowa do mapy mięśni (np. przedni, boczny i tylny akton barków). Każda należy do jednej
/// partii z <see cref="MuscleGroup"/> (objętość i filtry liczymy dalej po partiach ogólnych). Nie zmieniać kolejności.
/// </summary>
public enum MuscleDetail
{
    FrontDelts, SideDelts, RearDelts,
    UpperChest, LowerChest,
    Biceps, Triceps, Forearms,
    Abs, Obliques,
    UpperTraps, MidTraps, Rhomboids, Scapular, Lats, LowerBack,
    Glutes, GluteMedius, Adductors,
    Quads, Hamstrings,
    Calves, Tibialis,
    Neck
}
