namespace PTScheduler.Application.Scheduling;

/// <summary>Reguły „Powiadom mnie o wolnym terminie” — bez bazy, żeby dało się je przetestować.</summary>
public static class SlotWatchRules
{
    /// <summary>Najwcześniej tyle przed terminem dajemy znać — później nikt już nie zdąży.</summary>
    public static readonly TimeSpan MinLead = TimeSpan.FromHours(2);
    /// <summary>Terminy dalej niż dwa tygodnie nie są „okazją”.</summary>
    public const int MaxDaysAhead = 14;
    /// <summary>Jedno powiadomienie na klienta na tyle godzin — nawet gdy zwolni się kilka terminów.</summary>
    public static readonly TimeSpan Throttle = TimeSpan.FromHours(12);

    public static readonly string[] DayShort = ["Pn", "Wt", "Śr", "Cz", "Pt", "So", "Nd"];

    /// <summary>Bit dnia tygodnia (poniedziałek = 1 … niedziela = 64).</summary>
    public static int Bit(DayOfWeek day) => 1 << (((int)day + 6) % 7);

    /// <summary>Czy termin od <paramref name="start"/> na <paramref name="minutes"/> minut mieści się w wybranych dniach i godzinach.</summary>
    public static bool Matches(int days, TimeOnly from, TimeOnly to, DateTime start, int minutes)
    {
        if ((days & Bit(start.DayOfWeek)) == 0) return false;
        var begin = TimeOnly.FromDateTime(start);
        var end = begin.AddMinutes(minutes);
        return begin >= from && end <= to && end > begin; // end > begin: wizyta nie przechodzi przez północ
    }

    /// <summary>Czy termin jest jeszcze „okazją”: nie za blisko i nie za daleko.</summary>
    public static bool InWindow(DateTime start, DateTime localNow) =>
        start >= localNow + MinLead && start <= localNow.AddDays(MaxDaysAhead);

    public static bool CanNotify(DateTime? lastNotifiedAt, DateTime utcNow) =>
        lastNotifiedAt is null || utcNow - lastNotifiedAt.Value >= Throttle;

    /// <summary>„Pn, Śr, Pt 7:00–10:00” — krótki opis wyboru.</summary>
    public static string Describe(int days, TimeOnly from, TimeOnly to)
    {
        var names = Enumerable.Range(0, 7).Where(i => (days & (1 << i)) != 0).Select(i => DayShort[i]).ToList();
        var dayText = names.Count == 7 ? "Codziennie" : names.Count == 5 && (days & 0b11111) == 0b11111 ? "Pn–Pt" : string.Join(", ", names);
        return $"{dayText} {from:H\\:mm}–{to:H\\:mm}";
    }
}
