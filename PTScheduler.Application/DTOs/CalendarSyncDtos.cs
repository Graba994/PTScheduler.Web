namespace PTScheduler.Application.DTOs;

public sealed class CalendarConnectionDto
{
    /// <summary>Czy w tej instalacji da się połączyć Google (platforma skonfigurowana albo własne konto Google Meet).</summary>
    public bool Available { get; set; }
    /// <summary>„platform” — trener klika „Połącz z Google”; „own” — konto z ustawień Google Meet tej instalacji.</summary>
    public string Mode { get; set; } = "platform";
    public bool Connected { get; set; }
    public bool NeedsReconnect { get; set; }
    public string? GoogleEmail { get; set; }
    public bool PushSessions { get; set; } = true;
    public bool BlockBusy { get; set; } = true;
    public bool ShowClientName { get; set; } = true;
    public bool CreateMeetLinks { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }
    public int UpcomingBusyBlocks { get; set; }
}

public sealed record CalendarSyncResult(bool Ok, int Created, int Updated, int Deleted, int BusyBlocks, string? Error)
{
    public static CalendarSyncResult Fail(string error) => new(false, 0, 0, 0, 0, error);
}

public sealed record BusyBlockDto(DateTime StartTime, DateTime EndTime, bool IsAllDay);
