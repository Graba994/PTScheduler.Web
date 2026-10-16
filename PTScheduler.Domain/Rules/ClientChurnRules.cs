namespace PTScheduler.Domain.Rules;

/// <summary>Cel szkicu wiadomości do klienta — dobierany do najważniejszego powodu ryzyka.</summary>
public enum DraftPurpose
{
    /// <summary>Powrót po przerwie (dawno nie było treningu, nic nie zaplanowane).</summary>
    Comeback,
    /// <summary>Koniec pakietu — propozycja kolejnego.</summary>
    Package,
    /// <summary>Rzadsze treningi, odwołania — motywacja i wspólne ustalenie grafiku.</summary>
    Motivation,
    /// <summary>Gorsze samopoczucie, ból — troska i dopasowanie treningu.</summary>
    Wellbeing,
    /// <summary>Gratulacje postępów.</summary>
    Congrats,
    /// <summary>Przypomnienie o treningach z planu w aplikacji.</summary>
    PlanReminder,
    /// <summary>Uprzejme przypomnienie o zaległej płatności.</summary>
    Payment,
}

/// <summary>
/// Ryzyko odejścia klienta trenera z prostych sygnałów: przerwa bez zaplanowanych wizyt, spadek częstotliwości,
/// odwołania i nieobecności, koniec pakietu, gorsze samopoczucie i ból z ankiet, przerwa w treningach z planu.
/// Każdy powód ma wagę; sugestia i cel wiadomości pochodzą z najcięższego powodu.
/// </summary>
public static class ClientChurnRules
{
    public const int High = 50;
    public const int Medium = 30;

    public sealed record Input(
        int? DaysSinceLastVisit, bool HasUpcoming, int VisitsLast28, int VisitsPrev56, int CompletedTotal,
        int CancellationsLast30, int NoShowsLast30, int? PackageRemaining, bool PackageEndedRecently,
        bool WellbeingDropping, bool LowSatisfaction, bool RecentPain,
        int AppWorkoutsLast14, int AppWorkoutsPrev28);

    public sealed record Reason(int Points, string Text, DraftPurpose Purpose, string Suggestion);

    public sealed record Result(int Score, List<Reason> Reasons)
    {
        public bool IsHigh => Score >= High;
        public bool IsRisk => Score >= Medium;
        public Reason? Top => Reasons.Count > 0 ? Reasons[0] : null;
    }

    public static Result Score(Input i)
    {
        var found = new List<Reason>();

        if (!i.HasUpcoming && i.DaysSinceLastVisit is { } days)
        {
            if (days >= 21)
                found.Add(new(35, $"Ostatni trening {AttentionRules.Days(days)} temu, nic nie zaplanowane", DraftPurpose.Comeback,
                    "Napisz krótko i ciepło — zaproponuj konkretny termin powrotu."));
            else if (days >= 14)
                found.Add(new(20, $"Ostatni trening {AttentionRules.Days(days)} temu, nic nie zaplanowane", DraftPurpose.Comeback,
                    "Dobry moment na pytanie, kiedy widzimy się następnym razem."));
        }

        // Częstotliwość: tygodniowo w ostatnich 4 tygodniach vs w 8 tygodniach wcześniej (tylko przy stałych klientach).
        if (i.CompletedTotal >= 6 && i.VisitsPrev56 >= 8)
        {
            var before = i.VisitsPrev56 / 8m;
            var now = i.VisitsLast28 / 4m;
            if (now <= before / 2)
                found.Add(new(25, $"Rzadziej na treningach: {Week(now)}/tydz. (wcześniej {Week(before)})", DraftPurpose.Motivation,
                    "Zapytaj, co się zmieniło — może grafik albo cel wymaga odświeżenia."));
        }

        var missed = i.CancellationsLast30 + i.NoShowsLast30;
        if (missed >= 2)
            found.Add(new(missed >= 3 ? 20 : 15, $"{missed} {Plural(missed, "odwołanie lub nieobecność", "odwołania lub nieobecności", "odwołań lub nieobecności")} w 30 dni",
                DraftPurpose.Motivation, "Ustalcie stały termin, który na pewno pasuje — mniej przesuwania, mniej rezygnacji."));

        if (i.PackageRemaining == 0 && i.PackageEndedRecently && !i.HasUpcoming)
            found.Add(new(20, "Pakiet się skończył, brak nowego", DraftPurpose.Package,
                "Zaproponuj kolejny pakiet — najlepiej z podsumowaniem dotychczasowych postępów."));
        else if (i.PackageRemaining == 1)
            found.Add(new(10, "Zostało ostatnie wejście w pakiecie", DraftPurpose.Package,
                "Przy najbliższym treningu porozmawiajcie o kolejnym pakiecie."));

        if (i.LowSatisfaction)
            found.Add(new(15, "Niskie zadowolenie z treningów w ankietach", DraftPurpose.Wellbeing,
                "Porozmawiajcie o celach i planie — zadowolenie to najlepszy predyktor zostania."));
        if (i.WellbeingDropping)
            found.Add(new(10, "Samopoczucie po treningach spada", DraftPurpose.Wellbeing,
                "Zapytaj o sen, stres i regenerację; rozważ lżejszy tydzień."));
        if (i.RecentPain)
            found.Add(new(10, "Zgłoszony ból po treningu", DraftPurpose.Wellbeing,
                "Dopytaj o ból i dopasuj ćwiczenia, zanim klient zniechęci się do treningów."));

        if (i.AppWorkoutsPrev28 >= 4 && i.AppWorkoutsLast14 == 0)
            found.Add(new(10, $"Brak treningów z planu w aplikacji od 14 dni (wcześniej {i.AppWorkoutsPrev28} w 4 tyg.)", DraftPurpose.PlanReminder,
                "Przypomnij o planie albo zaproponuj krótszą wersję treningu."));

        var ordered = found.OrderByDescending(r => r.Points).ToList();
        return new(Math.Min(100, ordered.Sum(r => r.Points)), ordered);
    }

    private static string Week(decimal v) => v.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));

    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
