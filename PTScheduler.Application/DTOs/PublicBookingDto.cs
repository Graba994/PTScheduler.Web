namespace PTScheduler.Application.DTOs;

/// <summary>
/// Public-facing offer card. Returned by IPublicBookingService.GetActiveOfferAsync().
/// IsAvailable=false ⇒ show graceful "currently unavailable" page; UnavailableReason is for admins.
/// </summary>
public class PublicIntroOfferDto
{
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }

    public string TrainerUserId { get; set; } = string.Empty;
    public string TrainerName { get; set; } = string.Empty;

    public int DurationMinutes { get; set; }
    public bool IsFree { get; set; }
    public decimal Price { get; set; }
    public decimal? PromoPrice { get; set; }
    public DateTime? PromoValidUntil { get; set; }
    public string? Description { get; set; }

    public bool HasActivePromo =>
        PromoPrice.HasValue && PromoValidUntil.HasValue && PromoValidUntil.Value > DateTime.Now;

    public decimal EffectivePrice => HasActivePromo ? PromoPrice!.Value : Price;
}

public class BookingDayDto
{
    public DateOnly Date { get; set; }
    public string DayLabel { get; set; } = string.Empty;       // "pon, 12 maj"
    public List<BookingSlotDto> Slots { get; set; } = [];
}

public class BookingSlotDto
{
    public DateTime Start { get; set; }
    public string Label => Start.ToString("HH:mm");
}

public class CreatePublicBookingDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? TrainingGoal { get; set; }
    public DateTime SlotStart { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public bool AcceptedTerms { get; set; }
}

public class BookingResultDto
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ConfirmedSlot { get; set; }
    public string? ClientEmail { get; set; }
    public string? TrainerName { get; set; }
    public int DurationMinutes { get; set; }
    public bool EmailSent { get; set; }
}
