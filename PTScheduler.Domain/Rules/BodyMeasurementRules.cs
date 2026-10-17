namespace PTScheduler.Domain.Rules;

/// <summary>Sensowne zakresy pomiarów ciała — chronią przed literówkami (np. 750 zamiast 75,0 kg).</summary>
public static class BodyMeasurementRules
{
    public static List<string> Validate(DateOnly date, DateOnly today, decimal? weightKg, decimal? bodyFatPercent,
        params (string Label, decimal? Cm)[] circumferences)
    {
        var errors = new List<string>();
        if (date > today) errors.Add("Data pomiaru nie może być z przyszłości.");
        if (weightKg is null && bodyFatPercent is null && circumferences.All(c => c.Cm is null))
            errors.Add("Wpisz przynajmniej jedną wartość — np. wagę.");
        if (weightKg is < 20 or > 400) errors.Add("Waga powinna mieścić się w zakresie 20–400 kg.");
        if (bodyFatPercent is < 2 or > 75) errors.Add("Tkanka tłuszczowa powinna mieścić się w zakresie 2–75%.");
        foreach (var (label, cm) in circumferences)
            if (cm is < 5 or > 300) errors.Add($"{label}: wpisz wartość w zakresie 5–300 cm.");
        return errors;
    }
}
