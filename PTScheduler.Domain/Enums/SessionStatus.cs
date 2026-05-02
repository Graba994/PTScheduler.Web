namespace PTScheduler.Domain.Enums;

public enum SessionStatus
{
    Scheduled,
    Completed,
    Cancelled,
    NoShow,
    AwaitingPackage   // slot reserved, client has no remaining credits
}
