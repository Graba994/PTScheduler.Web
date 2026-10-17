namespace PTScheduler.Application.DTOs;

public class IntroSessionConfigDto
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 60;
    public bool IsFree { get; set; } = true;
    public decimal Price { get; set; } = 0;
    public decimal? PromoPrice { get; set; }
    public DateTime? PromoValidUntil { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // EffectivePrice usunięte — nie było używane, a liczyło ważność promocji
    // względem DateTime.UtcNow, czyli inaczej niż strona publiczna. Regułą
    // rozstrzygającą jest PTScheduler.Domain.Rules.PromoRules; jeśli cena
    // efektywna będzie tu kiedyś potrzebna, ustaw ją w serwisie przez tę regułę.
}

public class SaveIntroConfigDto
{
    public int DurationMinutes { get; set; } = 60;
    public bool IsFree { get; set; } = true;
    public decimal Price { get; set; } = 0;
    public decimal? PromoPrice { get; set; }
    public DateTime? PromoValidUntil { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
