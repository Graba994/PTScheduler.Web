namespace PTScheduler.Application.DTOs;

/// <summary>
/// Dane z kreatora rejestracji w Portalu („Zbuduj swoją aplikację”). Portal wysyła je zaraz po
/// uruchomieniu instancji (POST /internal/setup/bootstrap), więc trener nie wpisuje ich drugi raz.
/// Z gotowym skrótem hasła konto właściciela powstaje od razu i kreator /setup jest pomijany;
/// bez skrótu (instancja założona ręcznie w Portalu) dane tylko wypełniają /setup.
/// </summary>
public class SetupBootstrapDto
{
    public string CompanyName { get; set; } = "";
    public string OwnerEmail { get; set; } = "";
    public string? OwnerFirstName { get; set; }
    public string? OwnerLastName { get; set; }
    public string? OwnerPhone { get; set; }
    /// <summary>Skrót hasła w formacie ASP.NET Identity — Portal nie przechowuje samego hasła.</summary>
    public string? PasswordHash { get; set; }
    public string? SetupMode { get; set; }
    /// <summary>Kolor aplikacji (AppBranding.ThemeName, np. „ocean”).</summary>
    public string? AppTheme { get; set; }
    /// <summary>Szablon strony głównej (SiteWidgets.Templates, np. „personal”).</summary>
    public string? SiteTemplate { get; set; }
    public List<SetupOfferDto> Offers { get; set; } = [];
    public SetupPackageDto? Package { get; set; }
    /// <summary>SHA-256 (hex) jednorazowego tokenu „wejdź do aplikacji” zaraz po publikacji.</summary>
    public string? WelcomeTokenHash { get; set; }
    public DateTime? WelcomeTokenExpiresUtc { get; set; }
}

public class SetupOfferDto
{
    public string Name { get; set; } = "";
    public int DurationMinutes { get; set; } = 60;
    public decimal? Price { get; set; }
    public bool IsPair { get; set; }
    public bool IsGroup { get; set; }
    public int? MaxParticipants { get; set; }
}

public class SetupPackageDto
{
    public string Name { get; set; } = "";
    public int SessionsCount { get; set; }
    public decimal Price { get; set; }
    /// <summary>Którego treningu z <see cref="SetupBootstrapDto.Offers"/> dotyczy pakiet.</summary>
    public int OfferIndex { get; set; }
}

/// <summary>Wynik: Completed = konto właściciela istnieje i kreator /setup nie jest potrzebny.</summary>
public sealed record SetupBootstrapResult(bool Completed, string Message);

/// <summary>Dane do wypełnienia /setup (instancja założona bez hasła).</summary>
public sealed record SetupPrefillDto(string? CompanyName, string? Email, string? FirstName, string? LastName, string? Phone, string? SetupMode);
