using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>Komentarze do dni treningowych: trener daje informację zwrotną, klient odpowiada.</summary>
public interface IWorkoutCommentService
{
    Task<List<WorkoutCommentDto>> GetForClientAsync(int clientId);

    /// <summary>Dodaje komentarz i wysyła push drugiej stronie (best-effort).</summary>
    Task<WorkoutCommentDto> AddAsync(int clientId, DateOnly workoutDate, string authorUserId, bool byTrainer, string text);

    /// <summary>Usuwa komentarz — tylko autor.</summary>
    Task<bool> DeleteAsync(int commentId, string requesterUserId);

    /// <summary>Oznacza jako przeczytane komentarze drugiej strony.</summary>
    Task MarkReadAsync(int clientId, bool readerIsTrainer);
}
