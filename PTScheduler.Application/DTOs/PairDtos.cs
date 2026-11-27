namespace PTScheduler.Application.DTOs;

/// <summary>Para podopiecznych, którzy trenują razem.</summary>
public class PairDto
{
    public int Id { get; set; }
    public int ClientAId { get; set; }
    public string ClientAName { get; set; } = string.Empty;
    public int ClientBId { get; set; }
    public string ClientBName { get; set; } = string.Empty;
    /// <summary>Ile wspólnych treningów zostało w aktywnych pakietach pary.</summary>
    public int SharedRemaining { get; set; }
    public DateTime? NextTraining { get; set; }
    public string DisplayName => $"{ClientAName} + {ClientBName}";
}

/// <summary>Osoba, z którą podopieczny trenuje w parze.</summary>
public class PartnerDto
{
    public int ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
}

public record PartnerLookupResult(bool Success, int? ClientId, string? Name, string? Error, bool IsNewAccount = false);
