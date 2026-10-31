using PTScheduler.Domain.Enums;

namespace PTScheduler.Web.Services;

/// <summary>
/// Kolory mapy mięśni: każda rodzina partii ma swój kolor (klatka czerwona, barki pomarańczowe, ramiona niebieskie,
/// brzuch zielony, plecy fioletowe, pośladki różowe, uda morskie, łydki turkusowe, kark złoty), a mięśnie w rodzinie —
/// odcienie tego koloru. Partia główna ma pełny kolor, pomocnicza — jaśniejszy (CSS).
/// </summary>
public static class MuscleColors
{
    public enum Family { Chest, Shoulders, Arms, Core, Back, Hips, Thighs, LowerLeg, Neck }

    public static readonly (Family Family, string Label, string Color)[] Families =
    [
        (Family.Chest, "Klatka", "#e11d48"),
        (Family.Shoulders, "Barki", "#f97316"),
        (Family.Arms, "Ramiona", "#2563eb"),
        (Family.Core, "Brzuch", "#16a34a"),
        (Family.Back, "Plecy", "#7c3aed"),
        (Family.Hips, "Pośladki i biodra", "#db2777"),
        (Family.Thighs, "Uda", "#0d9488"),
        (Family.LowerLeg, "Łydki", "#0891b2"),
        (Family.Neck, "Kark", "#ca8a04"),
    ];

    public static Family Of(MuscleGroup g) => g switch
    {
        MuscleGroup.Chest => Family.Chest,
        MuscleGroup.Shoulders => Family.Shoulders,
        MuscleGroup.Biceps or MuscleGroup.Triceps or MuscleGroup.Forearms => Family.Arms,
        MuscleGroup.Abdominals => Family.Core,
        MuscleGroup.Traps or MuscleGroup.MiddleBack or MuscleGroup.Lats or MuscleGroup.LowerBack => Family.Back,
        MuscleGroup.Glutes or MuscleGroup.Abductors or MuscleGroup.Adductors => Family.Hips,
        MuscleGroup.Quadriceps or MuscleGroup.Hamstrings => Family.Thighs,
        MuscleGroup.Calves => Family.LowerLeg,
        _ => Family.Neck
    };

    public static Family Of(MuscleDetail d) => Of(PTScheduler.Domain.Rules.MuscleDetails.Group(d));

    public static string Color(MuscleGroup g) => g switch
    {
        MuscleGroup.Chest => "#e11d48",
        MuscleGroup.Shoulders => "#f97316",
        MuscleGroup.Biceps => "#2563eb",
        MuscleGroup.Triceps => "#1d4ed8",
        MuscleGroup.Forearms => "#3b82f6",
        MuscleGroup.Abdominals => "#16a34a",
        MuscleGroup.Traps => "#8b5cf6",
        MuscleGroup.MiddleBack => "#7c3aed",
        MuscleGroup.Lats => "#9333ea",
        MuscleGroup.LowerBack => "#a855f7",
        MuscleGroup.Glutes => "#db2777",
        MuscleGroup.Abductors => "#ec4899",
        MuscleGroup.Adductors => "#9d174d",
        MuscleGroup.Quadriceps => "#14b8a6",
        MuscleGroup.Hamstrings => "#0f766e",
        MuscleGroup.Calves => "#0891b2",
        _ => "#ca8a04"
    };

    public static string Color(MuscleDetail d) => d switch
    {
        // Odcienie w rodzinie są nasycone — jasny kolor zostaje zarezerwowany dla partii pomocniczych.
        MuscleDetail.FrontDelts => "#f97316",
        MuscleDetail.SideDelts => "#f59e0b",
        MuscleDetail.RearDelts => "#c2410c",
        MuscleDetail.UpperChest => "#f43f5e",
        MuscleDetail.LowerChest => "#be123c",
        MuscleDetail.Biceps => "#2563eb",
        MuscleDetail.Triceps => "#1d4ed8",
        MuscleDetail.Forearms => "#3b82f6",
        MuscleDetail.Abs => "#15803d",
        MuscleDetail.Obliques => "#22c55e",
        MuscleDetail.UpperTraps => "#8b5cf6",
        MuscleDetail.MidTraps => "#7c3aed",
        MuscleDetail.Rhomboids => "#6d28d9",
        MuscleDetail.Scapular => "#a855f7",
        MuscleDetail.Lats => "#9333ea",
        MuscleDetail.LowerBack => "#7e22ce",
        MuscleDetail.Glutes => "#db2777",
        MuscleDetail.GluteMedius => "#ec4899",
        MuscleDetail.Adductors => "#9d174d",
        MuscleDetail.Quads => "#14b8a6",
        MuscleDetail.Hamstrings => "#0f766e",
        MuscleDetail.Calves => "#0891b2",
        MuscleDetail.Tibialis => "#06b6d4",
        _ => "#ca8a04"
    };

    public static string FamilyColor(Family f) => Families.First(x => x.Family == f).Color;
    public static string FamilyLabel(Family f) => Families.First(x => x.Family == f).Label;
}
