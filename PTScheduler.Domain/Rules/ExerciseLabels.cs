namespace PTScheduler.Domain.Rules;

/// <summary>Polskie etykiety pól ćwiczenia z Free Exercise DB (sprzęt zapisany po angielsku).</summary>
public static class ExerciseLabels
{
    public static string Equipment(string eq) => eq.ToLowerInvariant() switch
    {
        "barbell" => "Sztanga",
        "dumbbell" => "Hantle",
        "cable" => "Wyciąg",
        "machine" => "Maszyna",
        "body only" => "Własnym ciężarem ciała",
        "kettlebells" => "Kettlebell",
        "bands" => "Gumy",
        "e-z curl bar" => "Gryf łamany",
        "exercise ball" => "Piłka gimnastyczna",
        "medicine ball" => "Piłka lekarska",
        "foam roll" => "Roller",
        "other" => "Inne",
        _ => eq
    };
}
