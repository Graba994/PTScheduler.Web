namespace PTScheduler.Domain.Entities;

/// <summary>
/// Jeden dzień planu treningowego (np. „Dzień A — push"). Uporządkowany
/// przez <see cref="Order"/>. Zawiera listę ćwiczeń.
/// </summary>
public class PlanDay
{
    public int Id { get; set; }

    public int PlanId { get; set; }
    public TrainingPlan? Plan { get; set; }

    public int Order { get; set; }
    public string Label { get; set; } = string.Empty;

    public ICollection<PlanExercise> Exercises { get; set; } = [];
}
