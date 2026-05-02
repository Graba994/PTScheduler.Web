namespace PTScheduler.Application.DTOs;

public class TrainerAvailabilityDto
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public DayOfWeek? DayOfWeek { get; set; }
    public DateOnly? SpecificDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string? Label { get; set; }
    public bool IsActive { get; set; }
    public bool IsRecurring => DayOfWeek.HasValue;
}

public class CreateTrainerAvailabilityDto
{
    public string TrainerUserId { get; set; } = string.Empty;
    public DayOfWeek? DayOfWeek { get; set; }
    public DateOnly? SpecificDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string? Label { get; set; }
}

public class TrainerConfigDto
{
    public int BreakAfterSessionMinutes { get; set; }
    public int SlotGranularityMinutes { get; set; } = 30;
    public bool AllowClientsDiscoverPeers { get; set; }
}

public class AvailableSlotDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAvailable { get; set; } = true;
}
