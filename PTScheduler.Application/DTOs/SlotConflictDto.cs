namespace PTScheduler.Application.DTOs;

/// <summary>
/// Opis sesji, która koliduje z proponowanym terminem. Zwracany przez
/// <see cref="Interfaces.ITrainerAvailabilityService.FindConflictAsync"/>,
/// żeby komunikat dla trenera był konkretny („koliduje z: Jan Kowalski,
/// 14:00”), a nie ogólnikowy.
/// </summary>
public sealed record SlotConflictDto(
    int SessionId,
    string ClientName,
    DateTime StartTime,
    int DurationMinutes);
