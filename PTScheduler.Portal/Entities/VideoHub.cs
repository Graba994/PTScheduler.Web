namespace PTScheduler.Portal.Entities;

/// <summary>
/// Plik wideo trenera w centralnej bibliotece Bunny Stream platformy. Rejestr w Portalu jest źródłem prawdy:
/// kto jest właścicielem filmu (usuwać i podglądać można tylko własne) i ile miejsca zajmuje.
/// Do limitu liczy się rozmiar przesłanego pliku — dokładnie tyle, ile trener wgrał (bez kopii po transkodowaniu Bunny).
/// </summary>
public class TenantVideo
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    /// <summary>GUID filmu w bibliotece Bunny.</summary>
    public string VideoGuid { get; set; } = string.Empty;
    /// <summary>Folder (kolekcja Bunny): kursy, cwiczenia.</summary>
    public string Kind { get; set; } = VideoKinds.Courses;
    public string Title { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class VideoKinds
{
    public const string Courses = "kursy";
    public const string Exercises = "cwiczenia";
    public static readonly string[] All = [Courses, Exercises];
    public static bool IsValid(string? kind) => kind is Courses or Exercises;
}

/// <summary>Kolekcja Bunny („folder”) jednego trenera dla danego rodzaju filmów: „{slug} · kursy”, „{slug} · cwiczenia”.</summary>
public class TenantVideoFolder
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Kind { get; set; } = VideoKinds.Courses;
    public string CollectionId { get; set; } = string.Empty;
}

/// <summary>
/// Ćwiczenie udostępnione przez trenera społeczności (za jego zgodą). Widzą je i kopiują do siebie trenerzy z pakietem
/// z wideo (Bunny). Wideo zostaje u autora — kopia tylko je osadza; gdy autor je usunie, kopie tracą film.
/// </summary>
public class CommunityExercise
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    /// <summary>Id ćwiczenia w instancji autora — ponowne udostępnienie aktualizuje wpis zamiast dublować.</summary>
    public int SourceExerciseId { get; set; }
    public string NamePl { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Description { get; set; }
    public string PrimaryMuscles { get; set; } = string.Empty;
    public string SecondaryMuscles { get; set; } = string.Empty;
    public string Category { get; set; } = "Strength";
    public string Level { get; set; } = "Beginner";
    public string? Equipment { get; set; }
    public string? Force { get; set; }
    public string? Mechanic { get; set; }
    public string? Tracking { get; set; }
    /// <summary>CSV pełnych adresów obrazów (z publicznego adresu instancji autora).</summary>
    public string ImageUrls { get; set; } = string.Empty;
    /// <summary>None, YouTube, Bunny, Url.</summary>
    public string VideoType { get; set; } = "None";
    public string? VideoRef { get; set; }
    /// <summary>Podpis autora (nazwa studia), gdy zgodził się go pokazać.</summary>
    public string? AuthorName { get; set; }
    public DateTime ConsentAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Ukryte przez administratora platformy (moderacja).</summary>
    public bool Hidden { get; set; }
    public int Copies { get; set; }
}
