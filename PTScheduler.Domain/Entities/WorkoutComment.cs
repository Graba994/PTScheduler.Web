namespace PTScheduler.Domain.Entities;

/// <summary>
/// Komentarz do dnia treningowego klienta (<see cref="WorkoutDate"/>) — trener
/// daje informację zwrotną („świetnie, następnym razem +2,5 kg”), klient może
/// odpowiedzieć. <see cref="ReadAt"/> ustawia druga strona po przeczytaniu.
/// </summary>
public class WorkoutComment
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>Dzień treningu (zegar ścienny), jak WorkoutLog.WorkoutDate.</summary>
    public DateOnly WorkoutDate { get; set; }

    public string AuthorUserId { get; set; } = string.Empty;

    /// <summary>True — napisał trener/asystent/admin; false — klient.</summary>
    public bool ByTrainer { get; set; }

    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
