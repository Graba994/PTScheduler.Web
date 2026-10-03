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
    public bool TrainerMessages { get; set; } = true;

    public bool ShowHints { get; set; } = true;

    public bool PushReminders { get; set; } = true;
    public bool PushSessions { get; set; } = true;
    public bool PushPackages { get; set; } = true;
    public bool PushMessages { get; set; } = true;
    public bool PushTrainerMessages { get; set; } = true;
    public bool PushClientActivity { get; set; } = true;
    public bool SmsReminders { get; set; } = true;
    public bool SmsTrainerMessages { get; set; } = true;
}
