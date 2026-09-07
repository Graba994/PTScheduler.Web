namespace PTScheduler.Application.DTOs;

/// <summary>
/// Editable content of the public Welcome (landing) page. Stored as a single
/// JSON blob in the persistent branding volume — new fields get defaults when
/// older JSON is loaded, so it stays backward compatible without a migration.
/// </summary>
public class SiteContentDto
{
    // ---- Global style ----
    public string Template { get; set; } = "classic";       // classic | gradient | dark | minimal
    public bool Animations { get; set; } = true;            // scroll-reveal + stat counters
    public string HeroBackground { get; set; } = "gradient"; // gradient | solid | image
    public string? HeroImageUrl { get; set; }
    public string? AccentOverride { get; set; }              // optional CSS color to override the brand accent

    // Global page colors (null = default / theme-aware).
    public string? PageBackground { get; set; }    // page background
    public string? HeadingColor { get; set; }      // all section titles
    public string? BodyTextColor { get; set; }     // paragraphs / muted text
    public string? ButtonTextColor { get; set; }   // primary button label
    // Hero gradient (used when HeroBackground = gradient).
    public string? HeroGradientFrom { get; set; }
    public string? HeroGradientTo { get; set; }

    // Order of the movable sections (hero is always first, footer always last).
    public List<string> SectionOrder { get; set; } =
        ["stats", "features", "about", "modules", "offer", "testimonials", "faq", "cta"];

    // ---- Hero ----
    public string? HeroEyebrow { get; set; } = "Trener personalny";
    public string HeroTitle { get; set; } = "Trening personalny i mentoring w jednym miejscu";
    public string HeroSubtitle { get; set; } =
        "Umawiaj sesje, rozliczaj się z trenerem i rozwijaj się z kursami online — wszystko w jednej aplikacji, bez chaosu i papierologii.";

    // Optional per-text colors (null = default / theme-aware).
    public string? HeroEyebrowColor { get; set; }
    public string? HeroTitleColor { get; set; }
    public string? HeroSubtitleColor { get; set; }
    public string PrimaryCtaLabel { get; set; } = "Umów sesję wstępną";
    public string PrimaryCtaUrl { get; set; } = "/book";
    public string? SecondaryCtaLabel { get; set; } = "Zobacz kursy";
    public string? SecondaryCtaUrl { get; set; } = "/courses";

    // ---- Stats ----
    public bool ShowStats { get; set; }
    public List<StatItem> Stats { get; set; } = [];

    // ---- Features ----
    public bool ShowFeatures { get; set; }
    public string FeaturesTitle { get; set; } = "Dlaczego warto";
    public List<FeatureItem> Features { get; set; } = [];

    // ---- About ----
    public bool ShowAbout { get; set; }
    public string AboutTitle { get; set; } = "O mnie";
    public string? AboutHtml { get; set; }
    public string? AboutImageUrl { get; set; }

    // ---- Modules section (scheduler + courses cards) ----
    public bool ShowModules { get; set; } = true;
    public string SectionTitle { get; set; } = "Co znajdziesz w środku";

    public bool ShowSchedulerCard { get; set; } = true;
    public string SchedulerCardTitle { get; set; } = "Trening & grafik";
    public string SchedulerCardText { get; set; } =
        "Rezerwacja sesji, kalendarz, pakiety treningowe, pomiary i rozliczenia. Trener zawsze wie, kto i kiedy przychodzi — a Ty masz wszystko pod ręką.";

    public bool ShowCoursesCard { get; set; } = true;
    public string CoursesCardTitle { get; set; } = "Kursy & mentoring";
    public string CoursesCardText { get; set; } =
        "Kursy online, lekcje wideo i materiały mentoringowe — dostępne dla Twoich klientów prosto z konta, z płatnościami online.";
    public bool CoursesComingSoon { get; set; } = true;

    public bool ShowPackagesCard { get; set; } = true;
    public string PackagesCardTitle { get; set; } = "Pakiety treningowe";
    public string PackagesCardText { get; set; } =
        "Kup pakiet sesji online — im więcej treningów, tym lepsza cena. Wygodna płatność, natychmiastowy dostęp do rezerwacji.";

    // ---- Offer / pricing ----
    public bool ShowOffer { get; set; }
    public string OfferTitle { get; set; } = "Oferta";
    public List<OfferItem> Offers { get; set; } = [];

    // ---- Testimonials ----
    public bool ShowTestimonials { get; set; }
    public string TestimonialsTitle { get; set; } = "Opinie";
    public List<TestimonialItem> Testimonials { get; set; } = [];

    // ---- FAQ ----
    public bool ShowFaq { get; set; }
    public string FaqTitle { get; set; } = "Najczęstsze pytania";
    public List<FaqItem> Faqs { get; set; } = [];

    // ---- CTA banner ----
    public bool ShowCta { get; set; }
    public string CtaTitle { get; set; } = "Gotowy, żeby zacząć?";
    public string? CtaText { get; set; } = "Umów pierwszą sesję i przekonaj się sam.";
    public string CtaButtonLabel { get; set; } = "Umów sesję";
    public string CtaButtonUrl { get; set; } = "/book";
    public string? CtaBgFrom { get; set; }
    public string? CtaBgTo { get; set; }
    public string? CtaTextColor { get; set; }
    public string? CtaButtonBg { get; set; }
    public string? CtaButtonTextColor { get; set; }

    // ---- Social media ----
    public bool ShowSocial { get; set; }
    public List<SocialLink> Socials { get; set; } = [];

    // ---- Footer ----
    public string? FooterText { get; set; }
}

public class SocialLink
{
    public string Icon { get; set; } = "bi-facebook"; // Bootstrap Icons class
    public string Url { get; set; } = "";
}

public class StatItem
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public class FeatureItem
{
    public string Icon { get; set; } = "bi-star";  // Bootstrap Icons class
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

public class OfferItem
{
    public string Name { get; set; } = "";
    public string Price { get; set; } = "";
    public string? Period { get; set; }
    public string? Description { get; set; }
    public List<string> Features { get; set; } = [];
    public string CtaLabel { get; set; } = "Wybieram";
    public string CtaUrl { get; set; } = "/book";
    public bool Highlighted { get; set; }
}

public class TestimonialItem
{
    public string Author { get; set; } = "";
    public string? Role { get; set; }
    public string Text { get; set; } = "";
}

public class FaqItem
{
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
}
