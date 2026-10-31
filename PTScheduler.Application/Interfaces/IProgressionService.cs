using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Automatyczna progresja: na podstawie zapisanych serii i RPE z ankiety po
/// treningu proponuje trenerowi zmianę docelowego ciężaru w planie klienta.
/// Nic nie zmienia samo — trener klika „Zastosuj" albo „Pomiń".
/// </summary>
public interface IProgressionService
{
    /// <summary>Aktualne propozycje dla klienta (tylko plany tego trenera, chyba że admin).</summary>
    Task<List<ProgressionSuggestionDto>> GetSuggestionsAsync(string trainerUserId, bool isAdmin, int clientId);

    /// <summary>Liczba propozycji per klient — do listy aktywności.</summary>
    Task<Dictionary<int, int>> GetCountsAsync(string trainerUserId, bool isAdmin);

    /// <summary>Ustawia nowy docelowy ciężar w planie i oznacza propozycję jako rozpatrzoną.</summary>
    Task<bool> ApplyAsync(string trainerUserId, bool isAdmin, int planExerciseId, decimal newTargetKg);

    /// <summary>Ukrywa propozycję do czasu kolejnego treningu z tym ćwiczeniem.</summary>
    Task<bool> DismissAsync(string trainerUserId, bool isAdmin, int planExerciseId);
}
