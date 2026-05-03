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
}
