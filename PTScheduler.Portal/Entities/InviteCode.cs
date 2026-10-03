namespace PTScheduler.Portal.Entities;

/// <summary>
/// Kod zaproszenia do kreatora rejestracji (Panel → Kody zaproszeń). Z kodem aplikacja uruchamia się
/// od razu, nawet gdy dzienny limit automatycznych uruchomień jest wyczerpany, i może dostać dłuższy okres próbny.
/// </summary>
public class InviteCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    /// <summary>Dla kogo / skąd (np. „szkoła trenerów XYZ”, „akcja Instagram wrzesień”).</summary>
    public string? Note { get; set; }
    public int ExtraTrialDays { get; set; }
    /// <summary>Ile razy można użyć (0 = bez limitu).</summary>
    public int MaxUses { get; set; } = 1;
    public int Uses { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsUsable(DateTime now) => IsActive && (MaxUses == 0 || Uses < MaxUses) && (ExpiresAt is null || ExpiresAt > now);
}
