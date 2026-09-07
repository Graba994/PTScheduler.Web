namespace PTScheduler.Infrastructure.Data.SeedData;

/// <summary>
/// Kształt jednego rekordu z Free Exercise DB (dist/exercises.json).
/// Deserializacja z camelCase (PropertyNameCaseInsensitive).
/// </summary>
internal sealed class FreeExerciseDbRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Force { get; set; }
    public string? Level { get; set; }
    public string? Mechanic { get; set; }
    public string? Equipment { get; set; }
    public string[] PrimaryMuscles { get; set; } = [];
    public string[] SecondaryMuscles { get; set; } = [];
    public string? Category { get; set; }
    public string[] Images { get; set; } = [];
    public string[] Instructions { get; set; } = [];
}
