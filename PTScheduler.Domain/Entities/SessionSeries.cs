namespace PTScheduler.Domain.Entities;

/// <summary>
/// Defines a recurring booking series (e.g., every Monday at 8:00).
/// Sessions are generated immediately upon creation (eager approach).
/// </summary>
public class SessionSeries
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string TrainerUserId { get; set; } = string.Empty;
    public int SessionTypeId { get; set; }
    public SessionType SessionType { get; set; } = null!;

    // Comma-separated DayOfWeek values, e.g. "1,3,5" = Mon,Wed,Fri
    public string RecurrenceDays { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = string.Empty;

    public ICollection<Session> Sessions { get; set; } = [];
}
