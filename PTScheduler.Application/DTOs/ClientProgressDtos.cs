using PTScheduler.Domain.Rules;

namespace PTScheduler.Application.DTOs;

public sealed record StrengthHighlightDto(string Exercise, decimal FromKg, decimal ToKg)
{
    public decimal DeltaKg => ToKg - FromKg;
}

public sealed record PlanInsightDto(string Exercise, int PlanId, string Planned, string LastDone, InsightKind Kind, string Finding, string Recommendation);

/// <summary>Postępy klienta policzone z dziennika treningów, sesji i pomiarów — same fakty, bez AI.</summary>
public sealed class ClientProgressDto
{
    public int CompletedSessions { get; init; }
    public int WorkoutDays { get; init; }
    public DateOnly? Since { get; init; }
    public List<StrengthHighlightDto> Strength { get; init; } = [];
    public decimal? WeightFromKg { get; init; }
    public decimal? WeightToKg { get; init; }
    public decimal? WaistFromCm { get; init; }
    public decimal? WaistToCm { get; init; }
    /// <summary>Ostatnie 30 dni: odbyte treningi i nieobecności (w tym późne odwołania).</summary>
    public int Attended30 { get; init; }
    public int Missed30 { get; init; }
    public List<PlanInsightDto> Insights { get; init; } = [];

    /// <summary>Jest o czym mówić: kilka treningów albo zapisane ćwiczenia z co najmniej dwóch dni.</summary>
    public bool HasData => CompletedSessions >= 3 || WorkoutDays >= 2;
}
