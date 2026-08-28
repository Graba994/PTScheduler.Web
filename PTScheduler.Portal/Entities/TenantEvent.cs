namespace PTScheduler.Portal.Entities;

public class TenantEvent
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public string EventType { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
}

public static class TenantEventTypes
{
    public const string Created = "created";
    public const string Provisioned = "provisioned";
    public const string Suspended = "suspended";
    public const string Resumed = "resumed";
    public const string Deleted = "deleted";
    public const string PlanChanged = "plan_changed";
    public const string PaymentReceived = "payment_received";
    public const string PaymentFailed = "payment_failed";
    public const string TrialStarted = "trial_started";
    public const string TrialExpired = "trial_expired";
    public const string TrialWarning = "trial_warning";
    public const string DomainChanged = "domain_changed";
    public const string HealthDown = "health_down";
    public const string HealthRecovered = "health_recovered";
    public const string InactivityWarning = "inactivity_warning";
    public const string InactivitySuspended = "inactivity_suspended";
    public const string GraceExtended = "grace_extended";
    public const string CleanupWarning = "cleanup_warning";
    public const string CleanupDeleted = "cleanup_deleted";
}
