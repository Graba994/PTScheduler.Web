namespace PTScheduler.Domain.Rules;

/// <summary>Progi listy „Wymaga uwagi” i automatycznych przypomnień o pakietach.</summary>
public static class AttentionRules
{
    /// <summary>Tyle dni bez wizyty (i bez zaplanowanej) — klient „wypada z rytmu”.</summary>
    public const int NoVisitDays = 14;

    /// <summary>Przy tylu (lub mniej) pozostałych wizytach — czas na kolejny pakiet.</summary>
    public const int LowCreditsThreshold = 2;

    /// <summary>Pakiet wygasający w tylu dniach trafia na listę trenera.</summary>
    public const int ExpiringDaysTrainer = 14;

    /// <summary>Klient dostaje przypomnienie, gdy pakiet wygasa w tylu dniach.</summary>
    public const int ExpiringDaysClient = 7;

    /// <summary>Tyle dni bez treningu przy przypisanym planie.</summary>
    public const int NotTrainingDays = 7;

    /// <summary>Treningi z tylu ostatnich dni bez komentarza trenera trafiają na listę.</summary>
    public const int NewWorkoutDays = 3;

    /// <summary>Pakiet zakończony w tym oknie bez następnego = „brak aktywnego pakietu”.</summary>
    public const int PackageEndedLookbackDays = 60;

    /// <summary>Przypomnienie „zostały Ci N treningi” — tylko gdy klient już coś z pakietu wykorzystał.</summary>
    public static bool ShouldNotifyLowCredits(int total, int used) =>
        used > 0 && total - used > 0 && total - used <= LowCreditsThreshold;

    /// <summary>„1 trening”, „2 treningi”, „5 treningów”.</summary>
    public static string Trainings(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = n == 1 ? "trening"
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? "treningi"
            : "treningów";
        return $"{n} {word}";
    }

    /// <summary>„1 wizyta”, „2 wizyty”, „5 wizyt”.</summary>
    public static string Visits(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = n == 1 ? "wizyta"
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? "wizyty"
            : "wizyt";
        return $"{n} {word}";
    }

    /// <summary>„1 dzień”, „3 dni”.</summary>
    public static string Days(int n) => n == 1 ? "1 dzień" : $"{n} dni";
}
