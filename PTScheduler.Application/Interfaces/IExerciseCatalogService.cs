using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Katalog ćwiczeń: przeglądanie/wyszukiwanie bazy wspólnej i ćwiczeń trenera,
/// ulubione („interesujące mnie"), ostatnio używane oraz CRUD własnych ćwiczeń.
/// Wszystkie operacje są w kontekście konkretnego trenera (widoczność i
/// preferencje są per-trener).
/// </summary>
public interface IExerciseCatalogService
{
    Task<List<ExerciseListItemDto>> SearchAsync(string trainerUserId, ExerciseFilterDto filter, int take = 300);

    Task<ExerciseDetailDto?> GetAsync(string trainerUserId, int id);

    /// <summary>Dodaje własne ćwiczenie trenera (Visibility = Mine). Zwraca Id.</summary>
    Task<int> CreateAsync(string trainerUserId, SaveExerciseDto dto);

    /// <summary>Edytuje własne ćwiczenie. Ćwiczeń bazowych nie da się edytować.</summary>
    Task UpdateAsync(string trainerUserId, SaveExerciseDto dto);

    /// <summary>Usuwa własne ćwiczenie, o ile nie jest użyte w planie.</summary>
    Task DeleteAsync(string trainerUserId, int id);

    /// <summary>Przełącza „interesujące mnie". Zwraca nowy stan (true = ulubione).</summary>
    Task<bool> ToggleFavoriteAsync(string trainerUserId, int exerciseId);

    /// <summary>Odświeża znacznik „ostatnio używane" (wołane przy użyciu w planie).</summary>
    Task MarkUsedAsync(string trainerUserId, int exerciseId);

    /// <summary>Sprzęt występujący w katalodze — do wypełnienia filtra.</summary>
    Task<List<string>> GetEquipmentOptionsAsync(string trainerUserId);
}
