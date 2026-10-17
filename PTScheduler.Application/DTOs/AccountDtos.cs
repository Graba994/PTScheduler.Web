namespace PTScheduler.Application.DTOs;

/// <summary>Wszystko, co pokazuje strona „Moje konto”.</summary>
public sealed class AccountOverviewDto
{
    public string UserId { get; init; } = "";
    public string Email { get; init; } = "";
    public bool EmailConfirmed { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string? Phone { get; init; }
    public string? AvatarUrl { get; init; }
    /// <summary>Główna rola: Admin, Trainer, Subordinate, Client.</summary>
    public string Role { get; init; } = "";
    public bool IsStaff => Role is "Admin" or "Trainer" or "Subordinate";

    // ── Bezpieczeństwo ──
    public bool HasPassword { get; init; }
    public int PasskeyCount { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public DateTime? LastLoginUtc { get; init; }

    // ── Klient ──
    public int? ClientId { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? TrainingGoal { get; init; }
    public DateTime? ClientSinceUtc { get; init; }
    public AccountTrainerDto? Trainer { get; init; }

    public string FullName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } n ? n : Email;
    public string Initials
    {
        get
        {
            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var i = string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
            return i.Length > 0 ? i : "?";
        }
    }
}

/// <summary>Kontakt do trenera klienta (karta „Mój trener”).</summary>
public sealed record AccountTrainerDto(string Name, string? Email, string? Phone, string? AvatarUrl);

public sealed class SaveAccountProfileDto
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? TrainingGoal { get; set; }
}
