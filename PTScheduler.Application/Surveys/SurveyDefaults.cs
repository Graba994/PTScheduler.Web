using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Surveys;

/// <summary>
/// Domyślne ankiety zgodne z praktyką treningu personalnego:
/// <list type="bullet">
/// <item>Zdrowotna — 7 pytań ogólnych kwestionariusza PAR-Q+ (odpowiedź „Tak” = konsultacja
/// z lekarzem przed zwiększeniem aktywności) + wywiad trenerski (urazy, leki, cele, sen, stres)
/// i potwierdzenie prawdziwości danych.</item>
/// <item>Po treningu — RPE sesji w skali Borga CR-10 i czas trwania (obciążenie wewnętrzne
/// sRPE × min, metoda Fostera), krótki kwestionariusz samopoczucia (energia, sen, zakwasy),
/// zgłoszenie bólu, zadowolenie i uwagi dla trenera.</item>
/// </list>
/// Klucze pytań są stałe — na nich opierają się statystyki i wnioski (<see cref="SurveyKeys"/>).
/// </summary>
public static class SurveyDefaults
{
    public static SurveyTemplateDto For(SurveyKind kind) => kind switch
    {
        SurveyKind.HealthIntake => HealthIntake(),
        SurveyKind.PostWorkout => PostWorkout(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private const string ParQ = "Stan zdrowia (PAR-Q+)";
    private const string Background = "Wywiad";
    private const string Lifestyle = "Styl życia i cele";

    public static SurveyTemplateDto HealthIntake() => new()
    {
        Kind = SurveyKind.HealthIntake,
        Name = "Ankieta zdrowotna",
        Intro = "Zanim zaczniemy, trener musi poznać Twój stan zdrowia — to podstawa bezpiecznego treningu. " +
                "Pierwsza część to międzynarodowy kwestionariusz PAR-Q+. Odpowiedź „Tak” nie wyklucza treningu: " +
                "oznacza, że przed zwiększeniem aktywności warto skonsultować się z lekarzem, a trener dopasuje plan. " +
                "Odpowiedzi widzi tylko Twój trener.",
        IsEnabled = true,
        Questions =
        [
            YesNoFlag("parq_heart", "Czy lekarz kiedykolwiek stwierdził u Ciebie chorobę serca lub nadciśnienie tętnicze?"),
            YesNoFlag("parq_chest_pain", "Czy odczuwasz ból w klatce piersiowej w spoczynku, podczas codziennych czynności lub podczas aktywności fizycznej?"),
            YesNoFlag("parq_dizziness", "Czy w ciągu ostatnich 12 miesięcy zdarzyła Ci się utrata równowagi z powodu zawrotów głowy lub utrata przytomności?",
                "Nie dotyczy zawrotów głowy przy intensywnym wysiłku lub zadyszce."),
            YesNoFlag("parq_chronic", "Czy zdiagnozowano u Ciebie inną chorobę przewlekłą (poza chorobą serca i nadciśnieniem)?",
                "Np. cukrzyca, astma, choroby tarczycy, nowotwór, osteoporoza."),
            YesNoFlag("parq_meds", "Czy obecnie przyjmujesz leki zalecone przez lekarza na chorobę przewlekłą?"),
            YesNoFlag("parq_joint", "Czy obecnie lub w ciągu ostatnich 12 miesięcy występował u Ciebie problem z kośćmi, stawami lub mięśniami (więzadła, ścięgna), który mógłby się nasilić przy większej aktywności?",
                "Nie dotyczy dawnych urazów, które już nie ograniczają ruchu."),
            YesNoFlag("parq_supervised", "Czy lekarz zalecił Ci aktywność fizyczną wyłącznie pod nadzorem medycznym?"),
            new()
            {
                Key = "pregnancy", Section = Background, Type = SurveyQuestionType.YesNo, FlagOnYes = true,
                Label = "Czy jesteś w ciąży lub w okresie do 6 miesięcy po porodzie?",
                Help = "Pytanie opcjonalne — pomaga dobrać bezpieczne ćwiczenia."
            },
            new()
            {
                Key = "injuries", Section = Background, Type = SurveyQuestionType.Text,
                Label = "Przebyte urazy, operacje i dolegliwości bólowe",
                Help = "Co, kiedy i czy nadal dokucza (np. „skręcenie kostki 2023, bez dolegliwości”, „ból lędźwi przy siedzeniu”)."
            },
            new()
            {
                Key = "medications", Section = Background, Type = SurveyQuestionType.Text,
                Label = "Przyjmowane leki i suplementy"
            },
            new()
            {
                Key = "other_health", Section = Background, Type = SurveyQuestionType.Text,
                Label = "Inne informacje o zdrowiu, które trener powinien znać",
                Help = "Np. alergie, migreny, problemy z krążeniem, przeciwwskazania od lekarza."
            },
            new()
            {
                Key = "activity_level", Section = Lifestyle, Type = SurveyQuestionType.SingleChoice, Required = true,
                Label = "Jak często obecnie ćwiczysz?",
                Options = ["Brak regularnej aktywności", "1–2 razy w tygodniu", "3–4 razy w tygodniu", "5 i więcej razy w tygodniu"]
            },
            new()
            {
                Key = "goals", Section = Lifestyle, Type = SurveyQuestionType.MultiChoice, Required = true,
                Label = "Twoje główne cele",
                Options = ["Redukcja tkanki tłuszczowej", "Budowa masy mięśniowej", "Siła", "Kondycja i wytrzymałość",
                           "Zdrowie i samopoczucie", "Mobilność i sprawność", "Powrót po kontuzji", "Przygotowanie do zawodów"]
            },
            new()
            {
                Key = "sleep_hours", Section = Lifestyle, Type = SurveyQuestionType.Number, Min = 0, Max = 14, Unit = "h",
                Label = "Ile średnio śpisz na dobę?", FlagAtOrBelow = 5
            },
            new()
            {
                Key = "stress", Section = Lifestyle, Type = SurveyQuestionType.Scale, Min = 1, Max = 10,
                MinLabel = "Bardzo niski", MaxLabel = "Bardzo wysoki",
                Label = "Poziom stresu na co dzień", FlagAtOrAbove = 8
            },
            new()
            {
                Key = "smoking", Section = Lifestyle, Type = SurveyQuestionType.YesNo,
                Label = "Czy palisz papierosy (także e-papierosy)?"
            },
            new()
            {
                Key = "emergency_contact", Section = Lifestyle, Type = SurveyQuestionType.Text,
                Label = "Kontakt w nagłym wypadku (imię i telefon)"
            },
            new()
            {
                Key = "consent", Type = SurveyQuestionType.YesNo, Required = true, MustBeYes = true,
                Label = "Potwierdzam, że podane informacje są prawdziwe, i poinformuję trenera o każdej zmianie stanu zdrowia."
            }
        ]
    };

    public static SurveyTemplateDto PostWorkout() => new()
    {
        Kind = SurveyKind.PostWorkout,
        Name = "Jak poszło?",
        Intro = "30 sekund po treningu — dzięki temu trener dopasuje obciążenie do Twojej formy.",
        IsEnabled = true,
        Questions =
        [
            new()
            {
                Key = SurveyKeys.Rpe, Type = SurveyQuestionType.Scale, Min = 0, Max = 10, Required = true, ShowInStats = true,
                Label = "Jak ciężki był trening?",
                Help = "Skala wysiłku (RPE): 3 — lekko, 5 — ciężko, 7 — bardzo ciężko, 10 — maksymalny wysiłek.",
                MinLabel = "Odpoczynek", MaxLabel = "Maksimum"
            },
            new()
            {
                Key = SurveyKeys.Duration, Type = SurveyQuestionType.Number, Min = 5, Max = 300, Unit = "min", ShowInStats = true,
                Label = "Ile trwał trening?"
            },
            new()
            {
                Key = SurveyKeys.Energy, Type = SurveyQuestionType.Scale, Min = 1, Max = 5, ShowInStats = true, FlagAtOrBelow = 1,
                Label = "Poziom energii", MinLabel = "Bardzo niski", MaxLabel = "Bardzo wysoki"
            },
            new()
            {
                Key = SurveyKeys.Sleep, Type = SurveyQuestionType.Scale, Min = 1, Max = 5, ShowInStats = true, FlagAtOrBelow = 1,
                Label = "Jakość snu ostatniej nocy", MinLabel = "Bardzo słaba", MaxLabel = "Bardzo dobra"
            },
            new()
            {
                Key = SurveyKeys.Soreness, Type = SurveyQuestionType.Scale, Min = 1, Max = 5, ShowInStats = true, FlagAtOrAbove = 5,
                Label = "Zakwasy i zmęczenie mięśni przed treningiem", MinLabel = "Brak", MaxLabel = "Bardzo duże"
            },
            new()
            {
                Key = SurveyKeys.Pain, Type = SurveyQuestionType.YesNo, FlagOnYes = true, Required = true,
                Label = "Czy podczas treningu pojawił się ból (inny niż zmęczenie mięśni)?"
            },
            new()
            {
                Key = SurveyKeys.PainLocation, Type = SurveyQuestionType.Text,
                Label = "Jeśli tak — gdzie i przy którym ćwiczeniu?"
            },
            new()
            {
                Key = SurveyKeys.Satisfaction, Type = SurveyQuestionType.Scale, Min = 1, Max = 5, ShowInStats = true,
                Label = "Zadowolenie z treningu", MinLabel = "Niskie", MaxLabel = "Wysokie"
            },
            new()
            {
                Key = SurveyKeys.Comment, Type = SurveyQuestionType.Text,
                Label = "Uwagi dla trenera"
            }
        ]
    };

    private static SurveyQuestion YesNoFlag(string key, string label, string? help = null) => new()
    {
        Key = key, Section = ParQ, Type = SurveyQuestionType.YesNo, Required = true, FlagOnYes = true,
        Label = label, Help = help
    };
}

/// <summary>Stałe klucze pytań, na których opierają się statystyki i wnioski.</summary>
public static class SurveyKeys
{
    public const string Rpe = "rpe";
    public const string Duration = "duration_min";
    public const string Energy = "energy";
    public const string Sleep = "sleep_quality";
    public const string Soreness = "soreness";
    public const string Pain = "pain";
    public const string PainLocation = "pain_location";
    public const string Satisfaction = "satisfaction";
    public const string Comment = "comment";

    /// <summary>Klucze, których usunięcie wyłącza część statystyk — edytor ostrzega.</summary>
    public static readonly IReadOnlySet<string> UsedInInsights =
        new HashSet<string> { Rpe, Duration, Energy, Sleep, Soreness, Pain, PainLocation, Satisfaction, Comment };
}
