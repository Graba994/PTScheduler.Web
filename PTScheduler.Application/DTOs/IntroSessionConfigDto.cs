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

    public decimal EffectivePrice => PromoPrice.HasValue && PromoValidUntil.HasValue && PromoValidUntil > DateTime.UtcNow
        ? PromoPrice.Value
        : Price;
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
