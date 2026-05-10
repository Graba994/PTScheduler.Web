namespace PTScheduler.Domain.Entities;

public class NotificationPreferences
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    public bool SessionBooked { get; set; } = true;
    public bool SessionCancelledByTrainer { get; set; } = true;
    public bool SessionRescheduled { get; set; } = true;
    public bool PackageAssigned { get; set; } = true;
    /// <summary>Client opt-in for the 24h-before-session reminder email.</summary>
    public bool SessionReminders { get; set; } = true;

    public bool ClientCancelledSession { get; set; } = true;
    public bool NewClientPending { get; set; } = true;
    public bool ExpiringPackages { get; set; } = true;

    public bool ShowHints { get; set; } = true;
}
