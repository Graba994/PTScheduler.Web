using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Client
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? TrainingGoal { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? TrainerUserId { get; set; }
    public ClientStatus Status { get; set; } = ClientStatus.Active;
    public bool AllowSelfBooking { get; set; } = false;
    public DateTime? TermsAcceptedAt { get; set; }

    public ICollection<Session> Sessions { get; set; } = [];
    public ICollection<BodyMeasurement> BodyMeasurements { get; set; } = [];
    public ICollection<TrainerNote> TrainerNotes { get; set; } = [];
    public ICollection<SessionPackage> Packages { get; set; } = [];
}
