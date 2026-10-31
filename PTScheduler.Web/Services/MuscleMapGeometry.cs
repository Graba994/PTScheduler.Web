using System.Globalization;
using System.Text.RegularExpressions;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Web.Services;

/// <summary>
/// Rysunek postaci z partiami mięśni (przód i tył) do mapy mięśni ćwiczenia. Kształty są opisane dla prawej połowy
/// ciała (x ≥ 100 w układzie 200×420) i odbijane lustrzanie. Sylwetkę kobiety daje to samo ciało przeskalowane
/// w poziomie zależnie od wysokości (węższe barki, szersze biodra) plus własny kształt klatki i włosy.
/// Partie odpowiadają kluczom Free Exercise DB (<see cref="MuscleGroup"/>).
/// </summary>
public static partial class MuscleMapGeometry
{
    public const double Width = 200, Height = 420, Gap = 24;

    public enum Part { Head, Torso, Arm }

    public sealed record Region(MuscleGroup Muscle, bool Back, Part Part, string Half, bool? Female = null);

    // ── Sylwetka ─────────────────────────────────────────────────────────────
    public const string HeadHalf = "M100,14 C112,14 122,24 122,37 C122,50 113,59 100,59 Z";
    public const string TorsoHalf =
        "M100,56 L110,58 L111,76 C124,80 140,83 150,89 C158,94 162,104 162,118 C160,126 150,130 143,128 " +
        "C140,150 136,172 133,192 C136,204 141,214 141,226 C142,260 140,296 136,322 C137,346 135,372 130,394 " +
        "C133,402 135,410 127,412 L107,412 C106,404 107,398 108,392 C106,370 104,348 106,326 C106,298 104,262 101,236 L100,236 Z";
    public const string ArmHalf =
        "M146,96 C156,98 163,110 164,126 C166,148 168,170 168,190 C171,214 175,238 176,262 C178,270 179,284 175,296 " +
        "C172,301 166,299 165,290 L163,268 C159,244 154,220 153,196 C151,174 148,152 144,132 Z";

    public static readonly (string Front, string Back) HairMale =
        ("M100,14 C114,14 123,23 122,35 C117,27 110,23 100,23 Z",
         "M100,14 C114,14 123,24 122,40 C116,46 108,48 100,48 Z");
    public static readonly (string Front, string Back) HairFemale =
        ("M100,11 C118,11 128,24 126,44 C125,58 122,70 120,80 L113,80 C116,68 118,56 118,44 C118,30 110,22 100,22 Z",
         "M100,10 C120,10 128,26 126,46 C125,62 122,74 118,84 L100,86 Z");

    // ── Partie mięśni ───────────────────────────────────────────────────────
    public static readonly Region[] Regions =
    [
        // Przód
        new(MuscleGroup.Neck, false, Part.Torso, "M104,62 C108,66 112,72 112,78 L108,80 C106,74 104,68 104,62 Z"),
        new(MuscleGroup.Traps, false, Part.Torso, "M111,74 C124,79 138,83 148,89 C140,90 132,92 126,94 C120,88 115,83 111,80 Z"),
        new(MuscleGroup.Shoulders, false, Part.Torso, "M148,89 C157,93 163,104 163,118 C164,128 160,134 154,135 C151,124 147,112 140,103 C136,98 132,95 126,94 C134,92 142,90 148,89 Z"),
        new(MuscleGroup.Chest, false, Part.Torso, "M102,98 C114,94 128,94 140,103 C146,111 149,121 147,131 C140,140 126,145 112,144 C106,143 102,141 102,138 Z", Female: false),
        new(MuscleGroup.Chest, false, Part.Torso, "M102,100 C114,96 128,96 140,104 C147,112 150,124 146,136 C140,148 126,152 114,149 C107,147 103,143 102,140 Z", Female: true),
        new(MuscleGroup.Biceps, false, Part.Arm, "M147,133 C154,135 160,146 163,163 C165,175 163,186 158,188 C154,184 151,172 149,160 C147,150 146,140 147,133 Z"),
        new(MuscleGroup.Forearms, false, Part.Arm, "M156,196 C163,198 169,212 172,232 C174,246 174,258 170,262 C166,256 162,240 159,224 C157,214 155,204 156,196 Z"),
        new(MuscleGroup.Abdominals, false, Part.Torso, "M102,148 L116,148 C118,170 118,194 115,222 C110,228 105,230 102,231 Z"),
        new(MuscleGroup.Abdominals, false, Part.Torso, "M119,149 C128,150 135,158 136,172 C135,188 131,204 122,216 C121,194 121,168 119,149 Z"),
        new(MuscleGroup.Abductors, false, Part.Torso, "M135,212 C140,218 142,226 141,238 C139,248 136,256 133,262 C131,246 131,228 135,212 Z"),
        new(MuscleGroup.Quadriceps, false, Part.Torso, "M108,242 C116,236 127,230 133,228 C140,252 139,290 133,318 C126,323 116,322 111,316 C107,292 106,266 108,242 Z"),
        new(MuscleGroup.Adductors, false, Part.Torso, "M101,238 L106,240 C105,262 106,284 109,304 C104,292 101,268 101,238 Z"),
        new(MuscleGroup.Calves, false, Part.Torso, "M110,336 C114,333 118,340 118,356 C117,370 114,380 112,384 C108,370 107,350 110,336 Z"),
        new(MuscleGroup.Calves, false, Part.Torso, "M130,330 C134,336 134,352 132,368 C130,376 128,380 126,382 C126,364 126,344 130,330 Z"),

        // Tył
        new(MuscleGroup.Neck, true, Part.Torso, "M101,60 L109,60 L110,72 L101,74 Z"),
        new(MuscleGroup.Traps, true, Part.Torso, "M101,66 L110,70 C122,78 136,83 148,89 C138,95 128,102 120,114 C112,130 106,150 101,166 Z"),
        new(MuscleGroup.Shoulders, true, Part.Torso, "M148,89 C157,93 163,104 163,118 C164,128 160,134 154,134 C151,122 146,110 138,102 C142,96 145,92 148,89 Z"),
        new(MuscleGroup.MiddleBack, true, Part.Torso, "M122,116 C129,107 139,103 144,108 C147,118 145,128 140,136 C134,138 127,136 121,132 Z"),
        new(MuscleGroup.MiddleBack, true, Part.Torso, "M103,112 L113,114 C113,126 111,138 107,148 L102,152 Z"),
        new(MuscleGroup.Lats, true, Part.Torso, "M109,152 C119,143 132,140 145,134 C147,154 141,178 131,197 C122,203 112,201 104,195 C104,179 105,163 109,152 Z"),
        new(MuscleGroup.LowerBack, true, Part.Torso, "M101,172 L110,168 C112,186 112,204 110,220 L101,222 Z"),
        new(MuscleGroup.Triceps, true, Part.Arm, "M147,133 C154,135 160,146 163,163 C165,175 163,186 158,188 C154,184 151,172 149,160 C147,150 146,140 147,133 Z"),
        new(MuscleGroup.Forearms, true, Part.Arm, "M156,196 C163,198 169,212 172,232 C174,246 174,258 170,262 C166,256 162,240 159,224 C157,214 155,204 156,196 Z"),
        new(MuscleGroup.Abductors, true, Part.Torso, "M119,214 C129,207 139,210 141,221 C136,224 130,223 125,222 C121,220 119,217 119,214 Z"),
        new(MuscleGroup.Glutes, true, Part.Torso, "M101,226 C113,220 131,219 139,229 C143,244 140,259 129,265 C117,268 107,264 101,258 Z"),
        new(MuscleGroup.Hamstrings, true, Part.Torso, "M106,270 C117,267 131,266 138,272 C140,292 137,308 132,320 C124,326 114,324 110,318 C106,300 104,286 106,270 Z"),
        new(MuscleGroup.Adductors, true, Part.Torso, "M101,264 L105,268 C105,284 106,298 109,312 C104,301 101,283 101,264 Z"),
        new(MuscleGroup.Calves, true, Part.Torso, "M110,332 C119,325 131,326 134,340 C136,356 132,370 126,382 C120,386 114,384 112,378 C108,362 106,346 110,332 Z"),
    ];

    /// <summary>Linie podziału (bez interakcji): kratka brzucha, kolana, kręgosłup.</summary>
    public static readonly (bool Back, string Half)[] Details =
    [
        (false, "M102,170 L117,169 M102,192 L117,191 M102,211 L116,210"),
        (false, "M115,326 C119,322 125,322 128,327"),
        (true, "M100,70 L100,226"),
        (true, "M114,322 C119,326 126,326 131,322"),
    ];

    // ── Przekształcenia ─────────────────────────────────────────────────────

    // Szerokość kobiecej sylwetki względem bazowej na danej wysokości (y): węższe barki i talia, szersze biodra.
    private static readonly (double Y, double K)[] FemaleWidth =
        [(56, 1), (80, .9), (150, .88), (195, .9), (228, 1.07), (275, 1.05), (330, .98), (420, .97)];

    private static double K(double y, Part part, bool female)
    {
        if (!female || part == Part.Head) return 1;
        if (part == Part.Arm) return .9;
        if (y <= FemaleWidth[0].Y) return FemaleWidth[0].K;
        for (var i = 1; i < FemaleWidth.Length; i++)
        {
            var (y1, k1) = FemaleWidth[i];
            if (y <= y1)
            {
                var (y0, k0) = FemaleWidth[i - 1];
                return k0 + (k1 - k0) * (y - y0) / (y1 - y0);
            }
        }
        return FemaleWidth[^1].K;
    }

    [GeneratedRegex(@"(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)")]
    private static partial Regex Point();

    /// <summary>Ścieżka gotowa do SVG: połowa (albo jej lustro), sylwetka kobiety i przesunięcie widoku (dx).</summary>
    public static string Path(string half, Part part, bool female, bool mirror, double dx) =>
        Point().Replace(half, m =>
        {
            var x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (mirror) x = Width - x;
            x = Width / 2 + (x - Width / 2) * K(y, part, female) + dx;
            return string.Create(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#}");
        });

    /// <summary>Obie połowy naraz (prawa + lustrzana lewa).</summary>
    public static string Both(string half, Part part, bool female, double dx) =>
        Path(half, part, female, false, dx) + " " + Path(half, part, female, true, dx);

    public static IEnumerable<Region> For(bool back, bool female) =>
        Regions.Where(r => r.Back == back && (r.Female is null || r.Female == female));

    /// <summary>Partie widoczne na danym widoku — do podpowiedzi „pokaż tył”.</summary>
    public static bool Visible(MuscleGroup m, bool back) => Regions.Any(r => r.Muscle == m && r.Back == back);
}
