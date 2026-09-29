using Microsoft.AspNetCore.Identity;

namespace PTScheduler.Infrastructure.Data;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>
    /// Hasło nadał ktoś inny (start systemu, Portal, admin) — po zalogowaniu użytkownik
    /// musi ustawić własne, zanim zobaczy resztę aplikacji.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Kiedy wgrano zdjęcie profilowe (null — brak zdjęcia; wartość służy też do odświeżania cache).</summary>
    public DateTime? AvatarUpdatedAt { get; set; }

    /// <summary>Sekretny token linku „Moje wizyty w kalendarzu” (klient). Nowy token unieważnia stary link.</summary>
    public string? CalendarFeedToken { get; set; }

    // For Subordinate role: points to the Trainer who manages this user
    public string? SupervisorId { get; set; }
    public ApplicationUser? Supervisor { get; set; }
    public ICollection<ApplicationUser> Subordinates { get; set; } = [];
}
