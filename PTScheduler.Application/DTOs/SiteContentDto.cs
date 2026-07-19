namespace PTScheduler.Application.DTOs;

/// <summary>
/// Editable content of the public Welcome (landing) page.
/// Stored as a single JSON blob in the persistent branding volume, so it
/// survives redeploys without requiring a database migration.
/// </summary>
public class SiteContentDto
{
    // Hero
    public string HeroTitle { get; set; } = "Trening personalny i mentoring w jednym miejscu";
    public string HeroSubtitle { get; set; } =
        "Umawiaj sesje, rozliczaj się z trenerem i rozwijaj się z kursami online — wszystko w jednej aplikacji, bez chaosu i papierologii.";
    public string PrimaryCtaLabel { get; set; } = "Umów sesję wstępną";

    // Modules section
    public string SectionTitle { get; set; } = "Co znajdziesz w środku";

    // Card 1 — scheduler
    public bool ShowSchedulerCard { get; set; } = true;
    public string SchedulerCardTitle { get; set; } = "Trening & grafik";
    public string SchedulerCardText { get; set; } =
        "Rezerwacja sesji, kalendarz, pakiety treningowe, pomiary i rozliczenia. Trener zawsze wie, kto i kiedy przychodzi — a Ty masz wszystko pod ręką.";

    // Card 2 — courses / mentoring
    public bool ShowCoursesCard { get; set; } = true;
    public string CoursesCardTitle { get; set; } = "Kursy & mentoring";
    public string CoursesCardText { get; set; } =
        "Kursy online, lekcje wideo i materiały mentoringowe — dostępne dla Twoich klientów prosto z konta, z płatnościami online.";
    public bool CoursesComingSoon { get; set; } = true;
}
