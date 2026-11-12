namespace PTScheduler.Portal.Entities;

/// <summary>Odpowiedź z publicznej ankiety dla trenerów (/ankieta) — wyniki w Panelu → Ankieta.</summary>
public class TrainerSurveyResponse
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Skąd przyszła odpowiedź (?zrodlo= w linku, np. „fb-trenerzy-warszawa”).</summary>
    public string? Source { get; set; }
    public string? WorkMode { get; set; }
    public string? Clients { get; set; }
    /// <summary>Jak dziś zapisują klientów (wiele odpowiedzi, po przecinku).</summary>
    public string? Booking { get; set; }
    public string? NoShows { get; set; }
    /// <summary>Co zabiera najwięcej czasu albo pieniędzy (wiele odpowiedzi, po przecinku).</summary>
    public string? Pains { get; set; }
    public string? ToolsSpend { get; set; }
    public string? WouldPay { get; set; }
    public string? Wish { get; set; }
    /// <summary>E-mail tylko za zgodą — zaproszenie do testów i wyniki ankiety.</summary>
    public string? Email { get; set; }
    public bool ContactConsent { get; set; }
}
