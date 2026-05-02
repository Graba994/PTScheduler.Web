namespace PTScheduler.Application.DTOs;

public class SessionSeriesDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string TrainerUserId { get; set; } = string.Empty;
    public int SessionTypeId { get; set; }
    public string SessionTypeName { get; set; } = string.Empty;
    public List<DayOfWeek> RecurrenceDays { get; set; } = [];
    public TimeOnly StartTime { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public int TotalSessionsCreated { get; set; }
    public int FutureSessionsCount { get; set; }
}

public class CreateSessionSeriesDto
{
    public int ClientId { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public int SessionTypeId { get; set; }
    public List<DayOfWeek> RecurrenceDays { get; set; } = [];
    public TimeOnly StartTime { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Notes { get; set; }
    public int? PackageId { get; set; }
}

public class SeriesPreviewDto
{
    public List<DateTime> ScheduledDates { get; set; } = [];
    public List<DateTime> ConflictDates { get; set; } = [];
    public int AvailableCredits { get; set; }
    public int WillBeScheduled { get; set; }
    public int WillBeAwaitingPackage { get; set; }
}
