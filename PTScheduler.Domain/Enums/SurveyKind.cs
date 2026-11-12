namespace PTScheduler.Domain.Enums;

public enum SurveyKind
{
    /// <summary>Ankieta zdrowotna przy rozpoczęciu współpracy (PAR-Q+ i wywiad trenerski).</summary>
    HealthIntake = 0,

    /// <summary>Krótka ankieta po treningu (RPE sesji, samopoczucie, ból).</summary>
    PostWorkout = 1
}
