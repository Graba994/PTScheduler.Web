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
    public bool IsLateCancellation { get; set; }
    public string? MeetingUrl { get; set; }

    // ── Trening w parze ──
    public Guid? PairGroupId { get; set; }
    /// <summary>Wizyta drugiej osoby z pary (ten sam trening).</summary>
    public int? PartnerSessionId { get; set; }
    public int? PartnerClientId { get; set; }
    public string? PartnerName { get; set; }
    public SessionStatus? PartnerStatus { get; set; }
    /// <summary>
    /// Druga wizyta tego samego treningu — w kalendarzu trenera pokazujemy trening raz
    /// (na wizycie prowadzącej), a tę pomijamy.
    /// </summary>
    public bool IsPairFollower { get; set; }
    /// <summary>Wizyta korzysta ze wspólnego pakietu, który za ten trening pobrała druga osoba.</summary>
    public bool SharesPackageSlot { get; set; }
    /// <summary>Trening rozliczany ze wspólnego pakietu pary (1 za trening).</summary>
    public bool PackageShared { get; set; }
    public bool IsPair => PairGroupId.HasValue;
    /// <summary>Partner nadal jest na treningu (nie odwołał).</summary>
    public bool PartnerActive => PartnerStatus is not null and not SessionStatus.Cancelled;
    /// <summary>„Ola + Piotr” dla treningu w parze, gdy obie osoby są zapisane.</summary>
    public string DisplayName => PartnerName is not null && PartnerActive && Status != SessionStatus.Cancelled
        ? $"{ClientName} + {PartnerName}" : ClientName;
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
    public int? MaxParticipants { get; set; }
    public bool IsPair { get; set; }
    public bool IsActive { get; set; } = true;
    public int SessionCount { get; set; }
}

public class CreateSessionTypeDto
{
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 60;
    public bool IsGroup { get; set; }
    public int? MaxParticipants { get; set; }
    public bool IsPair { get; set; }
}

public class ClientSummaryDto
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? TrainerUserId { get; set; }
}
