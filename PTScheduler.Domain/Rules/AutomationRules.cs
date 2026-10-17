namespace PTScheduler.Domain.Rules;

/// <summary>Reguły automatycznych wiadomości — czyste funkcje, łatwe do przetestowania.</summary>
public static class AutomationRules
{
    /// <summary>Urodziny dziś (29 lutego świętujemy 28 lutego w latach nieprzestępnych).</summary>
    public static bool IsBirthday(DateOnly? dateOfBirth, DateOnly today)
    {
        if (dateOfBirth is not { } dob) return false;
        if (dob.Month == 2 && dob.Day == 29 && !DateTime.IsLeapYear(today.Year))
            return today.Month == 2 && today.Day == 28;
        return dob.Month == today.Month && dob.Day == today.Day;
    }

    /// <summary>Powitanie: dzień kroku minął, a klient założył konto po włączeniu reguły (bez zaległych wysyłek do starych klientów).</summary>
    public static bool WelcomeDue(DateTime clientCreatedUtc, DateTime ruleEnabledUtc, int delayDays, DateTime nowUtc) =>
        clientCreatedUtc >= ruleEnabledUtc.AddMinutes(-5)
        && nowUtc >= clientCreatedUtc.AddDays(delayDays)
        && nowUtc < clientCreatedUtc.AddDays(delayDays + 3); // nie nadrabiamy tygodniowych zaległości

    /// <summary>Odzyskiwanie: ostatnia wizyta dawniej niż próg, nic nie zaplanowane, a ostatnia wiadomość dawniej niż przerwa.</summary>
    public static bool WinBackDue(DateTime? lastVisit, bool hasUpcoming, DateTime? lastSent, int delayDays, int cooldownDays, DateTime now) =>
        lastVisit is { } lv
        && !hasUpcoming
        && (now - lv).TotalDays >= delayDays
        && (lastSent is null || (now - lastSent.Value).TotalDays >= cooldownDays);

    /// <summary>Koniec pakietu: od zakończenia minęło tyle dni, brak nowego pakietu i brak wiadomości po tym końcu.</summary>
    public static bool PackageEndedDue(DateTime? endedAt, bool hasActivePackage, DateTime? lastSent, int delayDays, DateTime now) =>
        endedAt is { } ended
        && !hasActivePackage
        && (now - ended).TotalDays >= delayDays
        && (now - ended).TotalDays < delayDays + 30 // stare pakiety to już sprawa „Dawno Cię nie było”
        && (lastSent is null || lastSent.Value < ended);

    /// <summary>Wstawia pola do treści ({Imie}, {Kupon}…). Brakujące pola usuwamy, żeby nie zostały klamry.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (k, v) in values) text = text.Replace("{" + k + "}", v);
        return System.Text.RegularExpressions.Regex.Replace(text, @"\{[A-Za-z]+\}", "");
    }
}
