namespace PTScheduler.Portal.Entities;

/// <summary>Jedno wywołanie AI (OpenRouter): kto, do czego, ile tokenów i ile kosztowało — podstawa budżetu miesięcznego.</summary>
public class AiUsageLog
{
    public long Id { get; set; }
    public int? TenantId { get; set; }
    /// <summary>Np. „asystent”, „test”.</summary>
    public string Feature { get; set; } = "";
    public string Model { get; set; } = "";
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    /// <summary>Koszt w USD — z odpowiedzi OpenRouter, a gdy jej brak, szacowany z cen w ustawieniach.</summary>
    public decimal CostUsd { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
