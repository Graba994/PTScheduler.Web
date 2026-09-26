namespace PTScheduler.Domain.Enums;

/// <summary>
/// Kto widzi ćwiczenie. <see cref="Public"/> = baza wspólna lub udostępnione
/// wszystkim; <see cref="Mine"/> = prywatne ćwiczenie danego trenera
/// (owner = TrainerUserId).
/// </summary>
public enum ExerciseVisibility
{
    Public,
    Mine
}
