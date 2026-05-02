namespace PTScheduler.Domain.Entities;

/// <summary>
/// Defines when a trainer is available for bookings.
/// Either recurring (DayOfWeek set) or one-time (SpecificDate set).
/// </summary>
public class TrainerAvailability
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;

    // Recurring rule: which day of week (null for one-time)
    public DayOfWeek? DayOfWeek { get; set; }

    // One-time rule: specific date (null for recurring)
    public DateOnly? SpecificDate { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    // Optional validity window for recurring rules
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }

    public string? Label { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
