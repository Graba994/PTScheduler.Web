namespace PTScheduler.Application.DTOs;

/// <summary>Ready-made Welcome-page presets. Applying one fills the content;
/// the admin can then edit everything freely.</summary>
public static class SiteTemplates
{
    public static readonly (string Key, string Name, string Desc)[] All =
    [
        ("classic",  "Klasyczny",           "Jasny, czytelny, uniwersalny"),
        ("gradient", "Nowoczesny gradient",  "Odważne gradienty i zaokrąglenia"),
        ("dark",     "Ciemny / Fitness",     "Energetyczny, ciemne tło hero"),
        ("minimal",  "Minimalistyczny",      "Dużo przestrzeni, oszczędny styl"),
    ];

    public static SiteContentDto Build(string key)
    {
        var c = new SiteContentDto
        {
            HeroEyebrow = "Trener personalny",
            HeroTitle = "Zmień swoją formę z trenerem, który prowadzi Cię za rękę",
            HeroSubtitle = "Treningi personalne, plany i kursy online w jednym miejscu. Umów pierwszą sesję i zacznij już dziś.",
            PrimaryCtaLabel = "Umów sesję wstępną",
            PrimaryCtaUrl = "/book",
            SecondaryCtaLabel = "Zobacz kursy",
            SecondaryCtaUrl = "/courses",

            ShowStats = true,
            Stats =
            [
                new() { Value = "150+", Label = "zadowolonych klientów" },
                new() { Value = "8 lat", Label = "doświadczenia" },
                new() { Value = "20+",  Label = "kursów online" },
            ],

            ShowFeatures = true,
            FeaturesTitle = "Dlaczego warto",
            Features =
            [
                new() { Icon = "bi-calendar2-check", Title = "Wygodne umawianie", Text = "Rezerwuj sesje online, bez telefonów i wiadomości." },
                new() { Icon = "bi-graph-up-arrow",  Title = "Widoczne postępy",  Text = "Pomiary, statystyki i historia treningów w jednym miejscu." },
                new() { Icon = "bi-mortarboard",     Title = "Kursy online",      Text = "Rozwijaj się między treningami dzięki materiałom wideo." },
                new() { Icon = "bi-shield-check",    Title = "Bezpieczne płatności", Text = "Kupuj pakiety i kursy online, dostęp nadaje się automatycznie." },
            ],

            ShowAbout = true,
            AboutTitle = "O mnie",
            AboutHtml = "<p>Cześć! Jestem trenerem personalnym z pasją do pomagania ludziom w osiąganiu ich celów. Od lat prowadzę treningi indywidualne i tworzę kursy online.</p><p>Wierzę, że najlepsze efekty przychodzą dzięki systematyczności i dobremu planowi — i właśnie w tym Ci pomogę.</p>",

            ShowModules = true,
            SectionTitle = "Co znajdziesz w środku",

            ShowOffer = true,
            OfferTitle = "Oferta",
            Offers =
            [
                new() { Name = "Sesja pojedyncza", Price = "120 zł", Period = "/ trening", Description = "Idealne na start.", Features = ["Trening 1:1", "Plan na sesję", "Konsultacja"], CtaLabel = "Umów", CtaUrl = "/book" },
                new() { Name = "Pakiet 10 sesji", Price = "1000 zł", Period = "/ pakiet", Description = "Najczęściej wybierany.", Features = ["10 treningów 1:1", "Plan treningowy", "Wsparcie między sesjami"], CtaLabel = "Wybieram", CtaUrl = "/book", Highlighted = true },
                new() { Name = "Kurs online", Price = "od 199 zł", Period = "", Description = "Ucz się w swoim tempie.", Features = ["Dostęp do lekcji wideo", "Materiały do pobrania", "Quizy i postępy"], CtaLabel = "Zobacz kursy", CtaUrl = "/courses" },
            ],

            ShowTestimonials = true,
            TestimonialsTitle = "Opinie",
            Testimonials =
            [
                new() { Author = "Anna K.", Role = "klientka", Text = "Zrzuciłam 12 kg i w końcu czuję się dobrze we własnym ciele. Polecam!" },
                new() { Author = "Marek W.", Role = "klient", Text = "Konkretny plan, świetne podejście i realne efekty. Najlepszy trener." },
                new() { Author = "Kasia P.", Role = "kursantka", Text = "Kursy online są super — mogę ćwiczyć kiedy chcę i widzę postępy." },
            ],

            ShowFaq = true,
            FaqTitle = "Najczęstsze pytania",
            Faqs =
            [
                new() { Question = "Jak umówić pierwszą sesję?", Answer = "Kliknij przycisk umawiania sesji, wybierz termin i gotowe." },
                new() { Question = "Czy muszę mieć doświadczenie?", Answer = "Nie — plany dopasowuję do każdego poziomu, również początkujących." },
                new() { Question = "Jak działają kursy online?", Answer = "Kupujesz kurs, a dostęp nadaje się automatycznie po opłaceniu." },
            ],

            ShowCta = true,
            CtaTitle = "Gotowy, żeby zacząć?",
            CtaText = "Umów pierwszą sesję i przekonaj się sam.",
            CtaButtonLabel = "Umów sesję",
            CtaButtonUrl = "/book",
        };

        switch (key)
        {
            case "gradient":
                c.Template = "gradient"; c.HeroBackground = "gradient"; c.HeroEyebrow = "Trening & mentoring";
                break;
            case "dark":
                c.Template = "dark"; c.HeroBackground = "gradient";
                break;
            case "minimal":
                c.Template = "minimal"; c.HeroBackground = "solid";
                c.ShowStats = false; c.ShowTestimonials = false; c.ShowOffer = false; c.ShowFaq = false;
                break;
            default:
                c.Template = "classic"; c.HeroBackground = "gradient";
                break;
        }
        return c;
    }
}
