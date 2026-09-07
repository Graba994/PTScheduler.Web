namespace PTScheduler.Domain.Rules;

/// <summary>
/// Reguły decydujące o wysyłce przypomnień 24h. Wydzielone z
/// <c>SessionReminderService</c> jako czysta logika, żeby dało się je
/// przetestować bez całej infrastruktury tła (usługa żyje w warstwie Web
/// i korzysta z zależności rozwiązywanych ze scope, więc sama nie jest
/// jednostkowo testowalna).
/// </summary>
public static class ReminderPolicy
{
    /// <summary>
    /// Ile razy wysyłka kanału może zawieść, zanim porzucimy przypomnienie.
    /// Chroni przed wysyłką w nieskończoność, gdy np. SMTP jest trwale
    /// niedostępny dla jednej sesji.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Czy dany kanał należy próbować wysłać w tym cyklu: tylko gdy jest
    /// dostępny dla tej sesji (włączony, klient ma kontakt, nie zrezygnował)
    /// i nie został jeszcze wysłany.
    /// </summary>
    public static bool ShouldSend(bool channelApplicable, bool alreadySent)
        => channelApplicable && !alreadySent;

    /// <summary>
    /// Czy po tylu nieudanych próbach porzucić przypomnienie (oznaczyć jako
    /// obsłużone mimo niewysłania), zamiast ponawiać w kolejnych cyklach.
    /// </summary>
    public static bool ShouldGiveUp(int attempts) => attempts >= MaxAttempts;
}
