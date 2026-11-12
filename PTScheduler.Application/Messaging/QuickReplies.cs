namespace PTScheduler.Application.Messaging;

/// <summary>
/// Gotowe krótkie odpowiedzi trenera na wiadomość klienta (przyciski w powiadomieniu i podpowiedzi na czacie).
/// Reguły po słowach kluczowych — bez AI i bez wysyłania treści gdziekolwiek.
/// </summary>
public static class QuickReplies
{
    public static IReadOnlyList<string> For(string message)
    {
        var m = message.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(m.Contains);

        if (Has("chor", "gorączk", "przeziębi", "źle się czuj", "kontuzj"))
            return ["Zdrowiej szybko! Przełożymy, gdy poczujesz się lepiej 💪", "Dzięki za info, odpocznij"];
        if (Has("przełoż", "przesun", "inny termin", "zmienić termin", "zmiana terminu", "inną godzin"))
            return ["Jasne — wybierz nowy termin w aplikacji", "Sprawdzę grafik i dam znać"];
        if (Has("odwoł", "nie dam rady", "nie mogę", "nie przyjd", "nie będzie mnie"))
            return ["Jasne, dzięki za info!", "Dzięki — daj znać, kiedy pasuje kolejny termin"];
        if (Has("spóźn", "korek", "będę później", "parę minut"))
            return ["Spokojnie, czekam!", "Dzięki za info 👍"];
        if (Has("dzięk", "dziek", "super trening", "było super"))
            return ["Do zobaczenia! 💪", "Dzięki, dobra robota!"];
        if (Has("zapłac", "płatnoś", "przelew", "faktur", "rachun"))
            return ["Dzięki, sprawdzę i potwierdzę", "Super, dziękuję!"];
        if (m.Contains('?'))
            return ["Odpiszę za chwilę", "Dobre pytanie — omówimy na treningu"];
        return ["👍", "Odpiszę za chwilę"];
    }
}
