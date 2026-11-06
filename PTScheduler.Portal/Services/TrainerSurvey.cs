namespace PTScheduler.Portal.Services;

/// <summary>Pytania publicznej ankiety dla trenerów (/ankieta) — wspólne dla formularza i wyników w Panelu.</summary>
public static class TrainerSurvey
{
    public sealed record Question(string Key, string Text, string[] Options, bool Multi = false, int MaxPicks = 0, string? Hint = null);

    public static readonly Question[] Questions =
    [
        new("WorkMode", "Gdzie najczęściej prowadzisz treningi?",
            ["Na siłowni / w klubie", "We własnym studiu", "U klienta albo w plenerze", "Online", "Różnie — mieszam"]),
        new("Clients", "Ilu masz stałych klientów?",
            ["0–5", "6–15", "16–30", "31–60", "Ponad 60"]),
        new("Booking", "Jak klienci zapisują się do Ciebie na trening?",
            ["WhatsApp / Messenger / SMS", "Telefon", "Excel, kartka, notatki", "Kalendarz Google", "Booksy lub inny system rezerwacji", "Aplikacja dla trenerów", "Inaczej"],
            Multi: true, Hint: "Zaznacz wszystko, czego używasz."),
        new("NoShows", "Ile treningów w miesiącu przepada, bo klient nie przyszedł i nie odwołał?",
            ["Prawie żaden", "1–2", "3–5", "6–10", "Więcej niż 10"]),
        new("Pains", "Co zabiera Ci najwięcej czasu albo nerwów?",
            ["Umawianie i przesuwanie terminów", "Pilnowanie płatności i pakietów", "Nieobecności klientów", "Plany treningowe i postępy",
             "Zdobywanie nowych klientów", "Faktury i rozliczenia", "Odpisywanie na wiadomości"],
            Multi: true, MaxPicks: 3, Hint: "Wybierz do trzech."),
        new("ToolsSpend", "Ile płacisz dziś miesięcznie za narzędzia do pracy z klientami?",
            ["Nic", "Do 50 zł", "50–100 zł", "100–200 zł", "Ponad 200 zł"]),
        new("WouldPay", "Jaka cena miesięcznie byłaby dla Ciebie rozsądna za jedną aplikację: zapisy, płatności, przypomnienia, plany i strona pod Twoją marką?",
            ["Nie płacę za takie narzędzia", "Do 50 zł", "50–99 zł", "100–149 zł", "150 zł i więcej"])
    ];

    public static string? Get(Entities.TrainerSurveyResponse r, string key) => key switch
    {
        "WorkMode" => r.WorkMode, "Clients" => r.Clients, "Booking" => r.Booking, "NoShows" => r.NoShows,
        "Pains" => r.Pains, "ToolsSpend" => r.ToolsSpend, "WouldPay" => r.WouldPay, _ => null
    };

    public static void Set(Entities.TrainerSurveyResponse r, string key, string? value)
    {
        switch (key)
        {
            case "WorkMode": r.WorkMode = value; break;
            case "Clients": r.Clients = value; break;
            case "Booking": r.Booking = value; break;
            case "NoShows": r.NoShows = value; break;
            case "Pains": r.Pains = value; break;
            case "ToolsSpend": r.ToolsSpend = value; break;
            case "WouldPay": r.WouldPay = value; break;
        }
    }

    /// <summary>Odpowiedzi wielokrotnego wyboru zapisujemy po „|” (opcje mogą zawierać przecinki).</summary>
    public const char Separator = '|';
}
