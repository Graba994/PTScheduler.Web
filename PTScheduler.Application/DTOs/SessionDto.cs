using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

public class SessionDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public int SessionTypeId { get; set; }
    public string SessionTypeName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public string TrainerName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime => StartTime.AddMinutes(DurationMinutes);
    public SessionStatus Status { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
}

public class CreateSessionDto
{
    public int ClientId { get; set; }
    public int SessionTypeId { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string? Notes { get; set; }
}

public class SessionTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public bool IsGroup { get; set; }
}

public class ClientSummaryDto
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
