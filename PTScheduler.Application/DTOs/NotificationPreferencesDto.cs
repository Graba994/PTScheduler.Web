namespace PTScheduler.Application.DTOs;

public class NotificationPreferencesDto
{
    public bool SessionBooked { get; set; } = true;
    public bool SessionCancelledByTrainer { get; set; } = true;
    public bool SessionRescheduled { get; set; } = true;
    public bool PackageAssigned { get; set; } = true;
    public bool SessionReminders { get; set; } = true;
    public bool ClientCancelledSession { get; set; } = true;
    public bool NewClientPending { get; set; } = true;
    public bool ExpiringPackages { get; set; } = true;

    public bool ShowHints { get; set; } = true;
}
