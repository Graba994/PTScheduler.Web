namespace PTScheduler.Domain.Enums;

/// <summary>How a course enrollment came to exist.</summary>
public enum EnrollmentSource
{
    Manual,   // Granted by an admin/trainer
    Purchase  // Bought by the client (payment)
}
