using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Session
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int SessionTypeId { get; set; }
    public SessionType SessionType { get; set; } = null!;
    public string TrainerUserId { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }
    public SessionStatus Status { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public int? PackageId { get; set; }
    public SessionPackage? Package { get; set; }

    public int? SeriesId { get; set; }
    public SessionSeries? Series { get; set; }

    public string? MeetingUrl { get; set; }
    public string? CalendarEventId { get; set; }

    /// <summary>
    /// Ustawiane, gdy przypomnienie 24h zostało w pełni obsłużone (oba kanały
    /// wysłane, pominięte lub porzucone). Dopóki null, sesja jest kandydatem
    /// w kolejnym cyklu. Trwały dedup przeżywa restart usługi.
    /// </summary>
    public DateTime? ReminderSentAt { get; set; }

    /// <summary>
    /// Znacznik per-kanał: ustawiany, gdy przypomnienie e-mail zostało wysłane
    /// (lub kanał nie dotyczy tej sesji). Dzięki niemu częściowa awaria drugiego
    /// kanału nie powoduje ponownej wysyłki e-maila w kolejnym cyklu.
    /// </summary>
    public DateTime? ReminderEmailSentAt { get; set; }

    /// <summary>Znacznik per-kanał dla SMS — analogicznie do e-maila.</summary>
    public DateTime? ReminderSmsSentAt { get; set; }

    /// <summary>
    /// Liczba cykli, w których wysyłka któregoś kanału zawiodła. Po przekroczeniu
    /// limitu przypomnienie jest porzucane (oznaczane jako obsłużone), żeby nie
    /// ponawiać w nieskończoność.
    /// </summary>
    public int ReminderAttempts { get; set; }

    public ICollection<SessionInvitation> Invitations { get; set; } = [];
}
