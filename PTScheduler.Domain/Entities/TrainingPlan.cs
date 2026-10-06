namespace PTScheduler.Domain.Entities;

/// <summary>
/// Plan treningowy: albo szablon trenera (<see cref="IsTemplate"/> = true,
/// ClientId = null), albo plan przypisany konkretnemu klientowi. Składa się
/// z dni (<see cref="PlanDay"/>), a każdy dzień z ćwiczeń.
/// </summary>
public class TrainingPlan
{
    public int Id { get; set; }

    public string TrainerUserId { get; set; } = string.Empty;

    /// <summary>Null = szablon nieprzypisany. Inaczej klient, dla którego jest plan.</summary>
    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public bool IsTemplate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlanDay> Days { get; set; } = [];
}
