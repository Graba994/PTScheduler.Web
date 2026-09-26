namespace PTScheduler.Domain.Entities;

/// <summary>
/// Zajęty przedział z kalendarza Google (zegar ścienny, jak Session.StartTime).
/// Świadomie bez tytułu i opisu — do blokowania grafiku wystarczy sam czas,
/// a prywatne wpisy trenera nie lądują w bazie aplikacji.
/// </summary>
public class CalendarBusyBlock
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
}
