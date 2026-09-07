using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Exceptions;

/// <summary>
/// Rzucany, gdy dodawana lub przenoszona sesja nachodzi na inną sesję tego
/// samego trenera, a wywołujący nie zgodził się jawnie na nałożenie
/// (<c>allowOverlap: true</c>).
///
/// <para>
/// Osobny typ (a nie <see cref="InvalidOperationException"/>), żeby UI mógł
/// odróżnić kolizję terminu — na którą trener może świadomie przystać — od
/// zwykłego błędu. <see cref="Conflict"/> niesie szczegóły do komunikatu.
/// </para>
/// </summary>
public sealed class SlotConflictException(SlotConflictDto conflict)
    : Exception(
        $"Termin koliduje z inną sesją: {conflict.ClientName}, " +
        $"{conflict.StartTime:dd.MM.yyyy HH:mm}.")
{
    public SlotConflictDto Conflict { get; } = conflict;
}
