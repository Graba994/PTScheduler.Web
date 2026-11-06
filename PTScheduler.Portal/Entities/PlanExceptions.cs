namespace PTScheduler.Portal.Entities;

/// <summary>
/// Wyjątek dla jednego studia: funkcja albo limit ponad plan (np. „Garmin gratis dla Ani do końca roku”).
/// Działa jak podłoga — studio nigdy nie dostaje mniej niż plan; po <see cref="ExpiresAt"/> wraca do planu samo.
/// </summary>
public class TenantFeatureException
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    /// <summary>Pole katalogu funkcji (PlanFeatureCatalog), np. „Wearables”, „MaxClients”.</summary>
    public string Key { get; set; } = "";
    /// <summary>Wartość jako JSON: true, liczba albo tekst w cudzysłowie (np. "premium").</summary>
    public string ValueJson { get; set; } = "true";
    /// <summary>Dlaczego — widać w panelu i w historii studia.</summary>
    public string? Note { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public bool IsActive(DateTime utcNow) => ExpiresAt is null || ExpiresAt > utcNow;
}

/// <summary>Historia zmian planów z panelu: kto, kiedy, co się zmieniło i jak plan wyglądał przed zmianą.</summary>
public class PlanChangeLog
{
    public int Id { get; set; }
    /// <summary>Plan albo null dla ustawień wspólnych (widoczność funkcji, okres próbny nowych kont).</summary>
    public string? PlanId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? ChangedBy { get; set; }
    /// <summary>Zmiany w punktach, jedna na linię (to samo, co w podsumowaniu przed zapisem).</summary>
    public string Summary { get; set; } = "";
    /// <summary>Plan przed zmianą (JSON pól planu) — do wczytania z powrotem w macierzy.</summary>
    public string? BeforeJson { get; set; }
}
