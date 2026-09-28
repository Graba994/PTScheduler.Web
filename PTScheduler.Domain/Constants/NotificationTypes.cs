namespace PTScheduler.Domain.Constants;

public static class NotificationTypes
{
    public const string SessionBooked = nameof(SessionBooked);
    public const string SessionCancelledByTrainer = nameof(SessionCancelledByTrainer);
    public const string SessionRescheduled = nameof(SessionRescheduled);
    public const string PackageAssigned = nameof(PackageAssigned);
    public const string ClientCancelledSession = nameof(ClientCancelledSession);
    public const string NewClientPending = nameof(NewClientPending);
    public const string ExpiringPackages = nameof(ExpiringPackages);
    public const string SessionReminders = nameof(SessionReminders);
    /// <summary>Automatyczne wiadomości od trenera: powitanie, „dawno Cię nie było”, życzenia urodzinowe.</summary>
    public const string TrainerMessages = nameof(TrainerMessages);
}
