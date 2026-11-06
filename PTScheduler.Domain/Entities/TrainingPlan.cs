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

    /// <summary>„Po co ten plan” — cel planu własnymi słowami trenera; klient widzi go nad planem.</summary>
    public string? Purpose { get; set; }

    public bool IsTemplate { get; set; }

    /// <summary>Z trenerem, samodzielnie albo oba — decyduje, gdzie klient widzi plan.</summary>
    public Enums.PlanUse Use { get; set; } = Enums.PlanUse.Both;

    /// <summary>Kolejność planów klienta (trener układa ręcznie; mniejsza = wyżej).</summary>
    public int SortOrder { get; set; }

    /// <summary>Zakończony plan — znika z list klienta, zostaje w historii i dzienniku.</summary>
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PlanDay> Days { get; set; } = [];
}
