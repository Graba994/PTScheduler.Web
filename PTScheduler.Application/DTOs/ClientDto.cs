using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

public class ClientDto
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } n ? n : Email;
    public string Initials => $"{FirstName?.FirstOrDefault()}{LastName?.FirstOrDefault()}".ToUpperInvariant().Trim();
    public string? Phone { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? TrainingGoal { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalSessions { get; set; }
    public DateTime? LastSessionDate { get; set; }

    public ClientStatus Status { get; set; } = ClientStatus.Active;
    public bool AllowSelfBooking { get; set; }
    public string? TrainerUserId { get; set; }
    public SessionPackageSummaryDto? ActivePackage { get; set; }
}

public class CreateClientDto
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? TrainingGoal { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? TrainerUserId { get; set; }
}

public class UpdateClientDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? TrainingGoal { get; set; }
    public DateOnly? DateOfBirth { get; set; }
}

public class TrainerNoteDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string TrainerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
