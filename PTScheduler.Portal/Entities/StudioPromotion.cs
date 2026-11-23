namespace PTScheduler.Portal.Entities;

public enum StudioKind { Ads = 0, Post = 1, Video = 2 }

/// <summary>Writing — AI pracuje (kolejka w tle), Ready — gotowe do przejrzenia i edycji, Failed — błąd po 3 próbach.</summary>
public enum StudioStatus { Writing = 0, Ready = 1, Failed = 2 }

/// <summary>Materiał ze Studia reklam: teksty reklam (Meta/Google), post albo (później) film.</summary>
public class StudioPromotion
{
    public int Id { get; set; }
    public StudioKind Kind { get; set; }
    public StudioStatus Status { get; set; } = StudioStatus.Writing;
    public string Brief { get; set; } = "";
    /// <summary>Uwaga właściciela do poprawki — AI zmienia tylko to, czego dotyczy.</summary>
    public string? Comment { get; set; }
    /// <summary>Wynik (JSON wg rodzaju: StudioRules.Ads / StudioRules.Post). Edytowalny ręcznie.</summary>
    public string? ResultJson { get; set; }
    /// <summary>Czy wolno pokazać integrację z Garminem (wtedy z zasadami marki).</summary>
    public bool Garmin { get; set; }
    /// <summary>Powstało ręcznie (bez AI).</summary>
    public bool Manual { get; set; }
    public int? InstagramPostId { get; set; }
    public int Attempts { get; set; }
    public DateTime? RunAfter { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
