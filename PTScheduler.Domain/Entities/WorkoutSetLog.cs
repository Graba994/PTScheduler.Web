namespace PTScheduler.Domain.Entities;

/// <summary>
/// Pojedyncza seria w ramach wykonania (<see cref="WorkoutLog"/>):
/// numer serii, powtórzenia, ciężar. Jednostki spójnie kg / powtórzenia.
/// Objętość serii = Reps × WeightKg (zob. Domain.Rules.VolumeCalculator).
/// </summary>
public class WorkoutSetLog
{
    public int Id { get; set; }

    public int WorkoutLogId { get; set; }
    public WorkoutLog? WorkoutLog { get; set; }

    public int SetNumber { get; set; }
    public int Reps { get; set; }
    public decimal WeightKg { get; set; }
}
