namespace PTScheduler.Domain.Constants;

public static class DocumentKinds
{
    /// <summary>Trzeba zaakceptować, żeby korzystać z aplikacji (np. regulamin studia).</summary>
    public const string Required = "required";
    /// <summary>Zgoda, którą klient może wyrazić albo nie — i zmienić zdanie (np. publikacja wizerunku).</summary>
    public const string Consent = "consent";

    public sealed record Template(string Title, string Kind, string Content);

    /// <summary>Gotowe wzory do szybkiego dodania — trener dopasowuje je do siebie.</summary>
    public static readonly IReadOnlyList<Template> Templates =
    [
        new("Regulamin studia", Required,
            "1. Treningi odbywają się w umówionych terminach. Wizytę można odwołać bez opłaty zgodnie z zasadami odwoływania widocznymi w aplikacji.\n\n" +
            "2. Pakiety treningowe są imienne i ważne przez okres podany przy zakupie. Niewykorzystane treningi po terminie ważności przepadają.\n\n" +
            "3. Klient informuje trenera o każdej zmianie stanu zdrowia, urazach i przyjmowanych lekach, które mogą mieć wpływ na trening.\n\n" +
            "4. Klient ćwiczy zgodnie ze wskazówkami trenera i zgłasza każdy ból lub złe samopoczucie w trakcie treningu.\n\n" +
            "5. Dane osobowe są przetwarzane w celu prowadzenia treningów i rozliczeń, zgodnie z polityką prywatności."),
        new("Zgoda na publikację wizerunku", Consent,
            "Wyrażam zgodę na nieodpłatne wykorzystanie mojego wizerunku (zdjęcia i nagrania z treningów, zdjęcia sylwetki „przed i po”) " +
            "w mediach społecznościowych i na stronie trenera w celu promocji usług.\n\n" +
            "Zgodę mogę w każdej chwili wycofać w aplikacji — materiały nie będą wtedy dalej publikowane."),
        new("Oświadczenie o stanie zdrowia", Required,
            "Oświadczam, że nie są mi znane przeciwwskazania zdrowotne do udziału w treningach, a o wszelkich dolegliwościach, " +
            "urazach i zmianach stanu zdrowia poinformuję trenera przed treningiem.\n\n" +
            "W razie wątpliwości skonsultuję się z lekarzem przed rozpoczęciem lub zwiększeniem intensywności treningów."),
    ];
}
